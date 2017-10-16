using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngineInternal.Input;

public class EndlessTerrain : MonoBehaviour
{
    private const float Scale = 1;
    private const float ViewerMoveThresholdForChunkUpdate = 25f;
    private const float SqrViewerMoveThresholdForChunkUpdate = ViewerMoveThresholdForChunkUpdate * ViewerMoveThresholdForChunkUpdate;

    public LODInfo[] detailLevels;
    public static float maxViewDst = 450;

    public Transform viewer;
    public Material mapMaterial;

    public static Vector2 viewerPosition;
    private Vector2 _viewerPositionOld;

    private static MapGenerator _mapGenerator;
    private int _chunkSize;
    private int _chunksVisibleInViewDst;

    private Dictionary<Vector2, TerrainChunk> _terrainChunkDictionary = new Dictionary<Vector2, TerrainChunk>();
    private static List<TerrainChunk> _terrainChunksVisibleLastUpdate = new List<TerrainChunk>();

    void Start()
    {
        maxViewDst = detailLevels[detailLevels.Length - 1].visibleDstThreshold;
        _mapGenerator = FindObjectOfType<MapGenerator>();
        _chunkSize = MapGenerator.mapChunkSize - 1;
        _chunksVisibleInViewDst = Mathf.RoundToInt(maxViewDst / _chunkSize);

        UpdateVisibleChunks();
    }

    void Update()
    {
        viewerPosition = new Vector2(viewer.position.x, viewer.position.z) / Scale;

        if ((_viewerPositionOld - viewerPosition).sqrMagnitude > SqrViewerMoveThresholdForChunkUpdate)
        {
            _viewerPositionOld = viewerPosition;
            UpdateVisibleChunks();
        }
    }

    void UpdateVisibleChunks()
    {

        for (int i = 0; i < _terrainChunksVisibleLastUpdate.Count; i++)
            _terrainChunksVisibleLastUpdate[i].SetVisible(false);

        _terrainChunksVisibleLastUpdate.Clear();

        int currentChunkCoordX = Mathf.RoundToInt(viewerPosition.x / _chunkSize);
        int currentChunkCoordY = Mathf.RoundToInt(viewerPosition.y / _chunkSize);

        for (int yOffset = -_chunksVisibleInViewDst; yOffset <= _chunksVisibleInViewDst; yOffset++)
        {
            for (int xOffset = -_chunksVisibleInViewDst; xOffset <= _chunksVisibleInViewDst; xOffset++)
            {
                Vector2 viewedChunkCoord = new Vector2(currentChunkCoordX + xOffset, currentChunkCoordY + yOffset);
                if (_terrainChunkDictionary.ContainsKey(viewedChunkCoord))
                {
                    _terrainChunkDictionary[viewedChunkCoord].UpdateTerrainChunk();
                }
                else
                    _terrainChunkDictionary.Add(viewedChunkCoord, new TerrainChunk(viewedChunkCoord, _chunkSize, detailLevels, transform, mapMaterial));
            }
        }
    }


    public class TerrainChunk
    {
        private GameObject _meshObject;
        private Vector2 _position;
        private Bounds _bounds;

        private MeshRenderer _meshRenderer;
        private MeshFilter _meshFilter;

        private LODInfo[] _detailLevels;
        private LODMesh[] _lodMeshes;

        private MapData _mapData;
        private bool _mapDataReceived;
        private int _previousLODIndex = -1;

        public TerrainChunk(Vector2 coord, int size, LODInfo[] detailLevels, Transform parent, Material material)
        {
            _detailLevels = detailLevels;

            _position = coord * size;
            _bounds = new Bounds(_position, Vector2.one * size);
            Vector3 positionV3 = new Vector3(_position.x, 0, _position.y) * Scale;

            _meshObject = new GameObject("Terrain Chunk");
            _meshRenderer = _meshObject.AddComponent<MeshRenderer>();
            _meshFilter = _meshObject.AddComponent<MeshFilter>();

            _meshRenderer.material = material;
            _meshObject.transform.position = positionV3;
            _meshObject.transform.parent = parent;
            _meshObject.transform.localScale = Vector3.one * Scale;
            SetVisible(false);

            _lodMeshes = new LODMesh[detailLevels.Length];
            for (int i = 0; i < detailLevels.Length; i++)
            {
                _lodMeshes[i] = new LODMesh(detailLevels[i].lod, UpdateTerrainChunk);
            }

            _mapGenerator.RequestMapData(_position, OnMapDataReceived);
        }

        private void OnMapDataReceived(MapData mapData)
        {
            _mapData = mapData;
            _mapDataReceived = true;

            Texture2D texture = TextureGenerator.TextureFromColorMap(mapData.colorMap, MapGenerator.mapChunkSize, MapGenerator.mapChunkSize);
            _meshRenderer.material.mainTexture = texture;

            UpdateTerrainChunk();
        }

        void OnMeshDataReceived(MeshData meshData)
        {
            _meshFilter.mesh = meshData.CreateMesh();
        }

        public void UpdateTerrainChunk()
        {
            if (_mapDataReceived)
            {
                float viewerDstFromNearestEdge = Mathf.Sqrt(_bounds.SqrDistance(viewerPosition));
                bool visible = viewerDstFromNearestEdge <= maxViewDst;

                if (visible)
                {
                    int lodIndex = 0;
                    for (int i = 0; i < _detailLevels.Length - 1; i++)
                    {
                        if (viewerDstFromNearestEdge > _detailLevels[i].visibleDstThreshold)
                            lodIndex = i + 1;
                        else
                            break;
                    }

                    if (lodIndex != _previousLODIndex)
                    {
                        LODMesh lodMesh = _lodMeshes[lodIndex];
                        if (lodMesh.hasMesh)
                            _meshFilter.mesh = lodMesh.mesh;
                        else if (!lodMesh.hasRequestedMesh)
                            lodMesh.RequestMesh(_mapData);
                    }
                    _terrainChunksVisibleLastUpdate.Add(this);
                }

                SetVisible(visible);
            }

        }

        public void SetVisible(bool visible)
        {
            _meshObject.SetActive(visible);
        }

        public bool IsVisible()
        {
            return _meshObject.activeSelf;
        }
    }

    class LODMesh
    {
        public Mesh mesh;
        public bool hasRequestedMesh;
        public bool hasMesh;

        private int _lod;
        private Action _updateCallback;

        public LODMesh(int lod, Action updateCallback)
        {
            _lod = lod;
            _updateCallback = updateCallback;
        }

        private void OnMeshDataReceived(MeshData meshData)
        {
            mesh = meshData.CreateMesh();
            hasMesh = true;
            _updateCallback();
        }

        public void RequestMesh(MapData mapData)
        {
            hasRequestedMesh = true;
            _mapGenerator.RequestMeshData(mapData, _lod, OnMeshDataReceived);
        }
    }

    [Serializable]
    public struct LODInfo
    {
        public int lod;
        public float visibleDstThreshold;
    }
}
