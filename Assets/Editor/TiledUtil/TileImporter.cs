﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Xml.Linq;

using Mechanics;

using SuperTiled2Unity;
using SuperTiled2Unity.Editor;

using Cinemachine;
using ASK.Helpers;
using MyBox;
using Spawning;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

using World;
using Vector2 = UnityEngine.Vector2;
using LIL = TiledUtil.LayerImportLibrary;
using Random = System.Random;

namespace TiledUtil {

    [AutoCustomTmxImporter()]
    public class TileImporter : CustomTmxImporter, IFilterLoggerTarget {
        private Dictionary<String, GameObject> _prefabReplacements;

        public override void TmxAssetImported(TmxAssetImportedArgs data)
        {
            var typePrefabReplacements = data.AssetImporter.SuperImportContext.Settings.PrefabReplacements;
            _prefabReplacements = typePrefabReplacements.ToDictionary(so => so.m_TypeName, so => so.m_Prefab);
            SuperMap map = data.ImportedSuperMap;     
            
            //Don't import automap Tilesets
            if (map.gameObject.name.StartsWith("automap")) return;
            
            var args = data.AssetImporter;
            var layers = map.GetComponentsInChildren<SuperLayer>();
            var objects = map.GetComponentsInChildren<SuperObject>();
            var doc = XDocument.Load(args.assetPath);

            AddRoomComponents(map.transform);

            //Applies to the entire tilemap
            Dictionary<String, Action<GameObject>> tilemapLayerImports = new()
            {
                //{ "Lava", ImportLavaTilemap },
                { "Ground", ImportGroundTilemap },
                { "Semisolids", ImportSemisolidsTilemap },
                //{ "Water", ImportWaterTilemap },
                { "Dirt", ImportGroundTilemap },
                { "Windows", ImportWindowsTilemap },
                //{ "DecorBack", ImportDecorBackTilemap },
                //{ "GlowingMushrooms", ImportGlowingMushroomTilemap },
                { "Stalagtites", ImportStalagtitesTilemap },
                { "Spikes", ImportSpikesTilemap },
                { "Branches", ImportBranchesTilemap },
                //{ "Doors", ImportDoorsTilemap },
                //{ "Vines", ImportVinesTilemap },
            };
            
            //Applies to children
            Dictionary<String, Action<GameObject, int>> tileLayerImports = new() {
                { "Ground", ImportGround },
                { "Semisolids", ImportSemisolids },
                { "Dirt", ImportGround },
                { "Windows", ImportWindows },
                //{ "Breakable", ImportBreakable },
                //{ "GlowingMushrooms", ImportGlowingMushroom },
                // { "Stalagtites", ImportStalagtites },
                //{ "Lava", ImportLava },
                //{ "Water", ImportWater },
                //{ "Doors", ImportDoors },
            };
            
            foreach (SuperLayer layer in layers) {
                string layerName = layer.name;
                if (tilemapLayerImports.ContainsKey(layerName))
                {
                    tilemapLayerImports[layerName](layer.gameObject);
                }

                if (tileLayerImports.ContainsKey(layerName)) {
                  
                    ResolveTileLayerImports(layer.transform, tileLayerImports[layerName]);
                }
            }
        }

        private void ImportWindows(GameObject g, int index)
        {
            var data = LIL.TileToPrefab(g, index, _prefabReplacements["Windows"]);
            g = data.gameObject;
            Vector2[] colliderPoints = data.collisionPts;
            Vector2[] spritePoints = LIL.ColliderPointsToRectanglePoints(g, colliderPoints); 
            
            Vector2 avgSpritePoint = spritePoints.ComputeAverage();
            colliderPoints = colliderPoints.ComputeNormalized(avgSpritePoint);
            g.transform.localPosition = avgSpritePoint;
            
            // LIL.SetNineSliceSprite(g, spritePoints);
            // LIL.SetEdgeCollider2DPoints(g, colliderPoints);
            // LIL.AddShadowCast(g, colliderPoints.ToVector3());
            LIL.SetLayer(g, "Ground");
            g.GetRequiredComponent<SpriteRenderer>().SetSortingLayer("Interactable");
            // AddWaterfalCollision(g, colliderPoints);
        }

        private void ImportWindowsTilemap(GameObject g)
        {
            g.GetRequiredComponent<TilemapRenderer>().enabled = false;
        }

        private void AddRoomComponents(Transform room)
        {
            room.gameObject.AddComponent<Room>();
            room.gameObject.AddComponent<RoomSpawnSolver>();

            Tilemap mainTilemap = FindGroundLayerTilemap(room);
            if (mainTilemap == null)
            {
                FilterLogger.LogWarning(this,   $"Room bounds and components not added to tiled map {room.gameObject.name} " +
                                                $"because it does not contain a Tiled layer named 'Ground' or 'Dirt'.");
                return;
            }
            mainTilemap.CompressBounds();
            PolygonCollider2D bounds = AddPolygonColliderToRoom(room, mainTilemap);
            AddVCamManagerToRoom(room, bounds);
        }

        private Tilemap FindGroundLayerTilemap(Transform parent)
        {
            SuperTileLayer[] layers = parent.GetComponentsInChildren<SuperTileLayer>();
            foreach (SuperTileLayer layer in layers)
            {
                if (layer.gameObject.name.Equals("Ground") || layer.gameObject.name.Equals("Dirt"))
                {
                    return layer.GetComponent<Tilemap>();
                }
            }

            return null;
        }

        private PolygonCollider2D AddPolygonColliderToRoom(Transform room, Tilemap mainTilemap)
        {
            Bounds colliderBounds = mainTilemap.localBounds;

            PolygonCollider2D roomCollider = room.gameObject.AddComponent<PolygonCollider2D>();
            roomCollider.pathCount = 0;
            Vector2 boundsMin = colliderBounds.min;
            Vector2 boundsMax = colliderBounds.max;
            float alpha = 0.01f;
            roomCollider.SetPath(0, new Vector2[]
            {
                boundsMin - Vector2.one * alpha,
                boundsMin + Vector2.right * colliderBounds.extents.x * 2 + new Vector2(alpha, -alpha),
                boundsMax + Vector2.one * alpha,
                boundsMin + Vector2.up * colliderBounds.extents.y * 2 + new Vector2(-alpha, alpha),
            });
            roomCollider.offset = mainTilemap.transform.position;
            roomCollider.isTrigger = true;

            return roomCollider;
        }

        private void AddVCamManagerToRoom(Transform room, PolygonCollider2D boundingShape)
        {
            GameObject instance = _prefabReplacements["VCamManager"];
            instance = (GameObject)PrefabUtility.InstantiatePrefab(instance);
            instance.transform.SetParent(room);

            instance.GetComponent<VCamManager>().SetConfiner(boundingShape);
        }

        private GameObject AddWaterfalCollision(GameObject g, Vector2[] points)
        {
            GameObject waterfallReplace = _prefabReplacements["WaterfallCollider"];
            waterfallReplace = LIL.CreatePrefab(waterfallReplace, 0, g.transform);
            LIL.SetEdgeCollider2DPoints(waterfallReplace, points);
            return waterfallReplace;
        }

        private void ImportGround(GameObject g, int index) {
            var ret = LIL.TileToPrefab(g, index, _prefabReplacements["Ground"]);
            // AddWaterfalCollision(ret.gameObject, ret.collisionPts);
            LIL.SetLayer(ret.gameObject, "Ground");
        }
        
        private void ImportGlowingMushroom(GameObject g, int index)
        {
            var ret = LIL.TileToPrefab(g, index, _prefabReplacements["Glowing Mushroom"]);
            ret.gameObject.transform.position = ret.collisionPts[2] + new Vector2(4, -12);
            ret.gameObject.transform.localScale = new Vector3(Mathf.Round(UnityEngine.Random.value)*2-1, 1, 1);
        }

        // private void ImportStalagtites(GameObject g, int index)
        // {
            
        // }
        
        private void ImportSemisolids(GameObject g, int index) {
            var ret = LIL.TileToPrefab(g, index, _prefabReplacements["Semisolid"]);
            LIL.SetLayer(ret.gameObject, "Ground");
        }

        private void ImportBreakable(GameObject g, int index) {
            var data = LIL.TileToPrefab(g, index, _prefabReplacements["Breakable"]);
            g = data.gameObject;
            Vector2[] colliderPoints = data.collisionPts;
            Vector2[] spritePoints = LIL.ColliderPointsToRectanglePoints(g, colliderPoints); 
            
            Vector2 avgSpritePoint = spritePoints.ComputeAverage();
            colliderPoints = colliderPoints.ComputeNormalized(avgSpritePoint);
            g.transform.localPosition = avgSpritePoint;
            
            LIL.SetNineSliceSprite(g, spritePoints);
            LIL.SetEdgeCollider2DPoints(g, colliderPoints);
            LIL.AddShadowCast(g, colliderPoints.ToVector3());
            LIL.SetLayer(g, "Ground");
            g.GetRequiredComponent<SpriteRenderer>().SetSortingLayer("Interactable");
            AddWaterfalCollision(g, colliderPoints);
        }

        private void ImportDoors(GameObject g, int index)
        {
            var ret = LIL.TileToPrefab(g, index, _prefabReplacements["Door"]);
            LIL.SetLayer(ret.gameObject, "Default");
        }

        private void ImportLavaTilemap(GameObject g)
        {
            LIL.SetMaterial(g, "Lava");
            g.GetRequiredComponent<TilemapRenderer>().SetSortingLayer("Lava");
        }

        private void ImportBranchesTilemap(GameObject g)
        {
            g.GetRequiredComponent<TilemapRenderer>().SetSortingLayer("Above Ground Decor");
            // LayerImportLibrary.SetMaterial(g, "Mask_Graph");
        }

        private void ImportDoorsTilemap(GameObject g)
        {
            g.GetComponent<TilemapRenderer>().enabled = false;
            g.transform.parent = g.transform.parent.parent;
        }
        
        private void ImportVinesTilemap(GameObject g)
        {
            g.GetRequiredComponent<TilemapRenderer>().SetSortingLayer("Vines");
        }

        private void ImportSpikesTilemap(GameObject g)
        {
            GameObject.DestroyImmediate(g);
        }
        
        private void ImportDecorBackTilemap(GameObject g)
        {
            g.GetRequiredComponent<TilemapRenderer>().SetSortingLayer("Bg");
        }

        private void ImportGlowingMushroomTilemap(GameObject g)
        {
            g.GetRequiredComponent<TilemapRenderer>().enabled = false;
        }

        private void ImportStalagtitesTilemap(GameObject g)
        {
            var r = g.GetRequiredComponent<TilemapRenderer>();
            r.SetSortingLayer("Bg");
            r.sortingOrder = 5;
        }
        
        private void ImportGroundTilemap(GameObject g)
        {
            g.GetRequiredComponent<TilemapRenderer>().SetSortingLayer("Main");
        }
        
        private void ImportSemisolidsTilemap(GameObject g)
        {
            g.GetRequiredComponent<TilemapRenderer>().SetSortingLayer("Main");
        }

        private void ImportWaterTilemap(GameObject g)
        {
            g.SetLayerRecursively("Water");
            g.GetRequiredComponent<TilemapRenderer>().SetSortingLayer("Lava");
            LIL.SetMaterial(g, "Mask_Graph");
        }

        private void ResolveTileLayerImports(Transform layer, Action<GameObject, int> import) {
            if (layer.childCount > 0) {
                Transform t = layer.GetChild(0);
                t.ForEachChild(import);
            }
        }

        public XElement GetLayerXNode(XDocument doc, SuperLayer layer) {
            foreach (XElement xNode in doc.Element("map").Elements()) {
                XAttribute curName = xNode.Attribute("name");
                if (curName != null && curName.Value == layer.name) {
                    return xNode;
                }
            }

            return null;
        }

        public LogLevel GetLogLevel()
        {
            return LogLevel.Warning;
        }
    }
}