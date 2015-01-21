﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSP;
using FerramAerospaceResearch.FARCollections;
using ferram4;

namespace FerramAerospaceResearch.FARPartGeoUtil
{
    class CrossSectionCurveGenerator
    {
        public void GetCrossSectionalAreaCurves(Part p, out CrossSectionCurve xArea, out CrossSectionCurve yArea, out CrossSectionCurve zArea)
        {
            xArea = new CrossSectionCurve();
            yArea = new CrossSectionCurve();
            zArea = new CrossSectionCurve();

            List<Line> meshLines = GenerateLinesFromPart(p);
        }

        private CrossSectionCurve GenerateCrossSection(List<Line> meshLines, int numCrossSections)
        {
            CrossSectionCurve curve = new CrossSectionCurve();

            List<CrossSectionEvent> eventQueue = GenerateEventQueue(meshLines, numCrossSections);
            HashSet<Line> currentLines = new HashSet<Line>();

            for (int i = 0; i < eventQueue.Count; i++)
            {
                CrossSectionEvent currentEvent = eventQueue[i];
                if(currentEvent.crossSectionCut)
                {
                    //Calc cross section from current lines using convex hull algorithm
                }
                else
                {
                    Line currentLine = currentEvent.line;
                    if (!currentLines.Remove(currentLine))
                        currentLines.Add(currentLine);
                }
            }


            return curve;
        }

        private List<CrossSectionEvent> GenerateEventQueue(List<Line> meshLines, int numCrossSections)
        {
            float upperBound = float.NegativeInfinity, lowerBound = float.PositiveInfinity;
            LLRedBlackTree<CrossSectionEvent> eventQueue = new LLRedBlackTree<CrossSectionEvent>();

            for (int i = 0; i < meshLines.Count; i++)
            {
                Line line = meshLines[i];
                CrossSectionEvent point1Event = new CrossSectionEvent();
                CrossSectionEvent point2Event = new CrossSectionEvent();

                point1Event.line = line;
                point1Event.point = line.point1.y;
                point1Event.crossSectionCut = false;

                eventQueue.Insert(point1Event);

                point2Event.line = line;
                point2Event.point = line.point2.y;
                point2Event.crossSectionCut = false;

                eventQueue.Insert(point2Event);

                upperBound = Math.Max(upperBound, line.point1.y);
                upperBound = Math.Max(upperBound, line.point2.y);

                lowerBound = Math.Min(lowerBound, line.point1.y);
                lowerBound = Math.Min(lowerBound, line.point2.y);
            }

            float stepSize = (upperBound - lowerBound) / (float)numCrossSections;

            for (int i = 0; i < numCrossSections; i++)
            {
                CrossSectionEvent crossSectionCut = new CrossSectionEvent();
                crossSectionCut.point = stepSize * i + lowerBound;
                crossSectionCut.crossSectionCut = true;

                eventQueue.Insert(crossSectionCut);
            }

            return eventQueue.InOrderTraversal();
        }

        //This will provide a list of lines that make up the part's geometry, oriented so that they are in part-oriented space
        private List<Line> GenerateLinesFromPart(Part p)
        {
            Transform partTransform = p.transform;
            Bounds colliderBounds, meshBounds;

            colliderBounds = PartGeometryUtil.MergeBounds(p.GetColliderBounds(), partTransform);
            meshBounds = PartGeometryUtil.MergeBounds(p.GetRendererBounds(), partTransform);

            List<Vector3> vertexList;
            List<int> triangleIndices;

            //If the mesh shape is much larger than the colliders, then unfortunately, we have to use the raw mesh
            //Otherwise, use the collider because it has fewer verts and tris to work with
            if (UseMeshBounds(colliderBounds, meshBounds, 0.05f))
            {
                Transform[] meshTransforms = FARGeoUtil.PartModelTransformArray(p);
                Mesh[] meshes = new Mesh[meshTransforms.Length];

                for (int i = 0; i < meshTransforms.Length; i++)
                {
                    MeshFilter mf = meshTransforms[i].GetComponent<MeshFilter>();
                    if (mf == null)
                        continue;
                    meshes[i] = mf.sharedMesh;
                }
                vertexList = GetVertexList(meshes, meshTransforms, partTransform);
                triangleIndices = GetTriangleVerts(meshes);
            }
            else
            {
                MeshCollider[] meshColliders = p.GetComponents<MeshCollider>();
                Transform[] meshTransforms = new Transform[meshColliders.Length];
                Mesh[] meshes = new Mesh[meshColliders.Length];

                for (int i = 0; i < meshColliders.Length; i++)
                {
                    MeshCollider mc = meshColliders[i];
                    meshTransforms[i] = mc.transform;
                    meshes[i] = mc.sharedMesh;
                }
                vertexList = GetVertexList(meshes, meshTransforms, partTransform);
                triangleIndices = GetTriangleVerts(meshes);
            }

            return GenerateLinesFromVertsAndTris(vertexList, triangleIndices);
        }

        private List<Line> GenerateLinesFromVertsAndTris(List<Vector3> verts, List<int> triIndices)
        {
            HashSet<Line> lines = new HashSet<Line>();

            for (int i = 0; i < triIndices.Count; i += 3)
            {
                Vector3 vert1, vert2, vert3;
                vert1 = verts[triIndices[i]];
                vert2 = verts[triIndices[i + 1]];
                vert3 = verts[triIndices[i + 2]];

                Line line1, line2, line3;
                line1 = new Line(vert1, vert2);
                line2 = new Line(vert2, vert3);
                line3 = new Line(vert3, vert1);

                if (!lines.Contains(line1))
                    lines.Add(line1);
                if (!lines.Contains(line2))
                    lines.Add(line2);
                if (!lines.Contains(line3))
                    lines.Add(line3);
            }

            return lines.ToList();
        }

        private bool UseMeshBounds(Bounds colliderBounds, Bounds meshBounds, float relTolerance)
        {
            Vector3 absTolerance = (meshBounds.max - meshBounds.min) * relTolerance;

            Vector3 maxTest = meshBounds.max - colliderBounds.max;
            if (maxTest.x > absTolerance.x || maxTest.y > absTolerance.y || maxTest.z > absTolerance.z)
                return true;

            Vector3 minTest = meshBounds.min - colliderBounds.min;
            if (minTest.x > absTolerance.x || minTest.y > absTolerance.y || minTest.z > absTolerance.z)
                return true;

            return false;
        }

        private List<Vector3> GetVertexList(Mesh[] meshes, Transform[] meshTransforms, Transform partTransform)
        {
            List<Vector3> vertices = new List<Vector3>();

            for (int i = 0; i < meshes.Length; i++)
            {
                Mesh m = meshes[i];
                Matrix4x4 matrix = partTransform.worldToLocalMatrix * meshTransforms[i].localToWorldMatrix;

                for(int j = 0; j < m.vertices.Length; j++)
                {
                    Vector3 v = matrix.MultiplyPoint(m.vertices[j]);
                    vertices.Add(v);
                }
            }
            return vertices;
        }

        private List<int> GetTriangleVerts(Mesh[] meshes)
        {
            List<int> triIndices = new List<int>();
            int offset = 0;
            for (int i = 0; i < meshes.Length; i++)
            {
                int[] tri = meshes[i].triangles;
                for(int j = 0; j < tri.Length; j++)
                {
                    triIndices.Add(tri[i] + offset);
                }
                offset += meshes[i].vertexCount;
            }

            return triIndices;
        }
    }
}
