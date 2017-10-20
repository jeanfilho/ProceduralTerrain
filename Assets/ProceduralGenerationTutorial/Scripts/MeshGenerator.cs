using System;
using UnityEngine;

public static class MeshGenerator
{

    public static MeshData GenerateTerrainMesh(float[,] heightMap, MeshSettings meshSettings, int levelOfDetail)
    {
        int meshSimplificationIncrement = Mathf.Max(1, levelOfDetail * 2);

        int borderedSize = heightMap.GetLength(0);
        int meshSize = borderedSize - 2 * meshSimplificationIncrement;
        int meshSizeUnsimplified = borderedSize - 2;

        float topLeftX = (meshSizeUnsimplified - 1) / -2f;
        float topLeftZ = (meshSizeUnsimplified - 1) / 2f;

        int verticesPerLine = (meshSize - 1) / meshSimplificationIncrement + 1;

        MeshData meshData = new MeshData(verticesPerLine, meshSettings.useFlatShading);

        int[,] vertexIndicesMap = new int[borderedSize, borderedSize];
        int meshVertexIndex = 0;
        int borderedVertexIndex = -1;

        for (int y = 0; y < borderedSize; y += meshSimplificationIncrement)
        {
            for (int x = 0; x < borderedSize; x += meshSimplificationIncrement)
            {
                bool isBorderVertex = y == 0 || y == borderedSize - 1 || x == 0 || x == borderedSize - 1;
                if (isBorderVertex)
                {
                    vertexIndicesMap[x, y] = borderedVertexIndex;
                    borderedVertexIndex--;
                }
                else
                {
                    vertexIndicesMap[x, y] = meshVertexIndex;
                    meshVertexIndex++;
                }
            }
        }

        for (int y = 0; y < borderedSize; y += meshSimplificationIncrement)
        {
            for (int x = 0; x < borderedSize; x += meshSimplificationIncrement)
            {
                int vertexIndex = vertexIndicesMap[x, y];
                Vector2 percent = new Vector2((x - meshSimplificationIncrement) / (float)meshSize, (y - meshSimplificationIncrement) / (float)meshSize);
                float height = heightMap[x, y];
                Vector3 vertexPosition = new Vector3((topLeftX + percent.x * meshSizeUnsimplified) * meshSettings.meshScale, height, (topLeftZ - percent.y * meshSizeUnsimplified) * meshSettings.meshScale);

                meshData.AddVertex(vertexPosition, percent, vertexIndex);

                if (x < borderedSize - 1 && y < borderedSize - 1)
                {
                    int a = vertexIndicesMap[x, y];
                    int b = vertexIndicesMap[x + meshSimplificationIncrement, y];
                    int c = vertexIndicesMap[x, y + meshSimplificationIncrement];
                    int d = vertexIndicesMap[x + meshSimplificationIncrement, y + meshSimplificationIncrement];
                    meshData.AddTriangle(a, d, c);
                    meshData.AddTriangle(d, a, b);
                }
            }
        }
        meshData.ProcessMesh();
        return meshData;
    }
}

public class MeshData
{
    private Vector3[] _vertices;
    private int[] _triangles;
    private Vector2[] _uvs;
    private Vector3[] _bakedNormals;

    private Vector3[] _borderVertices;
    private int[] _borderTriangles;

    private int _triangleIndex;
    private int _borderTriangleIndex;

    private bool _useFlatShading;

    public MeshData(int verticesPerLine, bool useFlatShading)
    {
        _useFlatShading = useFlatShading;
        _vertices = new Vector3[verticesPerLine * verticesPerLine];
        _uvs = new Vector2[verticesPerLine * verticesPerLine];
        _triangles = new int[(verticesPerLine - 1) * (verticesPerLine - 1) * 6];

        _borderVertices = new Vector3[verticesPerLine * 4 + 4];
        _borderTriangles = new int[verticesPerLine * 4 * 6];
    }

    public void AddVertex(Vector3 vertexPosition, Vector2 uv, int vertexIndex)
    {
        if (vertexIndex < 0)
        {
            _borderVertices[-vertexIndex - 1] = vertexPosition;
        }
        else
        {
            _vertices[vertexIndex] = vertexPosition;
            _uvs[vertexIndex] = uv;
        }
    }

    public void AddTriangle(int a, int b, int c)
    {
        if (a < 0 || b < 0 || c < 0)
        {
            _borderTriangles[_borderTriangleIndex] = a;
            _borderTriangles[_borderTriangleIndex + 1] = b;
            _borderTriangles[_borderTriangleIndex + 2] = c;
            _borderTriangleIndex += 3;
        }
        else
        {
            _triangles[_triangleIndex] = a;
            _triangles[_triangleIndex + 1] = b;
            _triangles[_triangleIndex + 2] = c;
            _triangleIndex += 3;
        }
    }

    private Vector3[] CalculateNormals()
    {
        Vector3[] vertexNormals = new Vector3[_vertices.Length];
        int triangleCount = _triangles.Length / 3;
        for (int i = 0; i < triangleCount; i++)
        {
            int normalTriangleIndex = i * 3;
            int vertexIndexA = _triangles[normalTriangleIndex];
            int vertexIndexB = _triangles[normalTriangleIndex + 1];
            int vertexIndexC = _triangles[normalTriangleIndex + 2];

            Vector3 triangleNormal = SurfaceNormalFromIndices(vertexIndexA, vertexIndexB, vertexIndexC);
            vertexNormals[vertexIndexA] += triangleNormal;
            vertexNormals[vertexIndexB] += triangleNormal;
            vertexNormals[vertexIndexC] += triangleNormal;
        }

        int borderTriangleCount = _borderTriangles.Length / 3;
        for (int i = 0; i < borderTriangleCount; i++)
        {
            int normalTriangleIndex = i * 3;
            int vertexIndexA = _borderTriangles[normalTriangleIndex];
            int vertexIndexB = _borderTriangles[normalTriangleIndex + 1];
            int vertexIndexC = _borderTriangles[normalTriangleIndex + 2];

            Vector3 triangleNormal = SurfaceNormalFromIndices(vertexIndexA, vertexIndexB, vertexIndexC);
            if (vertexIndexA >= 0)
                vertexNormals[vertexIndexA] += triangleNormal;
            if (vertexIndexB >= 0)
                vertexNormals[vertexIndexB] += triangleNormal;
            if (vertexIndexC >= 0)
                vertexNormals[vertexIndexC] += triangleNormal;
        }

        for (int i = 0; i < vertexNormals.Length; i++)
        {
            vertexNormals[i].Normalize();
        }

        return vertexNormals;
    }

    private Vector3 SurfaceNormalFromIndices(int indexA, int indexB, int indexC)
    {
        Vector3 pointA = indexA < 0 ? _borderVertices[-indexA - 1] : _vertices[indexA];
        Vector3 pointB = indexB < 0 ? _borderVertices[-indexB - 1] : _vertices[indexB];
        Vector3 pointC = indexC < 0 ? _borderVertices[-indexC - 1] : _vertices[indexC];

        Vector3 sideAB = pointB - pointA;
        Vector3 sideAC = pointC - pointA;

        return Vector3.Cross(sideAB, sideAC).normalized;
    }

    public void ProcessMesh()
    {
        if (_useFlatShading)
            FlatShading();
        else
            BakeNormals();
    }

    void BakeNormals()
    {
        _bakedNormals = CalculateNormals();
    }

    void FlatShading()
    {
        Vector3[] flatShadedVertices = new Vector3[_triangles.Length];
        Vector2[] flatShadedUVs = new Vector2[_triangles.Length];

        for (int i = 0; i < _triangles.Length; i++)
        {
            flatShadedVertices[i] = _vertices[_triangles[i]];
            flatShadedUVs[i] = _uvs[_triangles[i]];
            _triangles[i] = i;
        }

        _vertices = flatShadedVertices;
        _uvs = flatShadedUVs;
    }

    public Mesh CreateMesh()
    {
        Mesh mesh = new Mesh
        {
            vertices = _vertices,
            triangles = _triangles,
            uv = _uvs
        };
        if (_useFlatShading)
            mesh.RecalculateNormals();
        else
            mesh.normals = _bakedNormals;

        return mesh;
    }
}
