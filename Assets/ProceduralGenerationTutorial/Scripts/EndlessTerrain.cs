using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngineInternal.Input;
using TerrainData = UnityEngine.TerrainData;

public class EndlessTerrain : MonoBehaviour
{
    private const float ViewerMoveThresholdForChunkUpdate = 25f;

    private const float SqrViewerMoveThresholdForChunkUpdate =
        ViewerMoveThresholdForChunkUpdate * ViewerMoveThresholdForChunkUpdate;

    private const float ColliderGenerationDstThreshold = 5f;

    public int colliderLODIndex = 0;
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
    private static List<TerrainChunk> _visibleTerrainChunks = new List<TerrainChunk>();

    void Start()
    {
        maxViewDst = detailLevels[detailLevels.Length - 1].visibleDstThreshold;
        _mapGenerator = FindObjectOfType<MapGenerator>();
        _chunkSize = _mapGenerator.mapChunkSize - 1;
        _chunksVisibleInViewDst = Mathf.RoundToInt(maxViewDst / _chunkSize);

        UpdateVisibleChunks();
    }

    void Update()
    {
        viewerPosition = new Vector2(viewer.position.x, viewer.position.z) / _mapGenerator.terrainData.uniformScale;

        if (viewerPosition != _viewerPositionOld)
        {
            foreach (TerrainChunk chunk in _visibleTerrainChunks)
            {
                chunk.UpdateCollisionMesh();
            }
        }

        if ((_viewerPositionOld - viewerPosition).sqrMagnitude > SqrViewerMoveThresholdForChunkUpdate)
        {
            _viewerPositionOld = viewerPosition;
            UpdateVisibleChunks();
        }
    }

    void UpdateVisibleChunks()
    {
        HashSet<Vector2> alreadyUpdatedChunkCoords = new HashSet<Vector2>();
        for (int i = _visibleTerrainChunks.Count - 1; i >= 0; i--)
        {
            alreadyUpdatedChunkCoords.Add(_visibleTerrainChunks[i].coordinate);
            _visibleTerrainChunks[i].UpdateTerrainChunk();
        }

        _visibleTerrainChunks.Clear();

        int currentChunkCoordX = Mathf.RoundToInt(viewerPosition.x / _chunkSize);
        int currentChunkCoordY = Mathf.RoundToInt(viewerPosition.y / _chunkSize);

        for (int yOffset = -_chunksVisibleInViewDst; yOffset <= _chunksVisibleInViewDst; yOffset++)
        {
            for (int xOffset = -_chunksVisibleInViewDst; xOffset <= _chunksVisibleInViewDst; xOffset++)
            {
                Vector2 viewedChunkCoord = new Vector2(currentChunkCoordX + xOffset, currentChunkCoordY + yOffset);
                if (!alreadyUpdatedChunkCoords.Contains(viewedChunkCoord))
                {
                    if (_terrainChunkDictionary.ContainsKey(viewedChunkCoord))
                    {
                        _terrainChunkDictionary[viewedChunkCoord].UpdateTerrainChunk();
                    }
                    else
                        _terrainChunkDictionary.Add(viewedChunkCoord,
                            new TerrainChunk(viewedChunkCoord, _chunkSize, detailLevels, colliderLODIndex, transform,
                                mapMaterial));
                }
            }
        }
    }


    public class TerrainChunk
    {
        public Vector2 coordinate;

        private GameObject _meshObject;
        private Vector2 _position;
        private Bounds _bounds;

        private MeshRenderer _meshRenderer;
        private MeshFilter _meshFilter;
        private MeshCollider _meshCollider;

        private LODInfo[] _detailLevels;
        private LODMesh[] _lodMeshes;
        private int _colliderLODIndex;

        private MapData _mapData;
        private bool _mapDataReceived;
        private int _previousLODIndex = -1;
        private bool _hasSetCollider;

        public TerrainChunk(Vector2 coord, int size, LODInfo[] detailLevels, int colliderLODIndex, Transform parent,
            Material material)
        {
            coordinate = coord;
            _detailLevels = detailLevels;
            _colliderLODIndex = colliderLODIndex;
            _position = coord * size;
            _bounds = new Bounds(_position, Vector2.one * size);
            Vector3 positionV3 = new Vector3(_position.x, 0, _position.y) * _mapGenerator.terrainData.uniformScale;

            _meshObject = new GameObject("Terrain Chunk");
            _meshRenderer = _meshObject.AddComponent<MeshRenderer>();
            _meshFilter = _meshObject.AddComponent<MeshFilter>();
            _meshCollider = _meshObject.AddComponent<MeshCollider>();

            _meshRenderer.material = material;
            _meshObject.transform.position = positionV3;
            _meshObject.transform.parent = parent;
            _meshObject.transform.localScale = Vector3.one * _mapGenerator.terrainData.uniformScale;
            SetVisible(false);

            _lodMeshes = new LODMesh[detailLevels.Length];
            for (int i = 0; i < detailLevels.Length; i++)
            {
                _lodMeshes[i] = new LODMesh(detailLevels[i].lod);
                _lodMeshes[i].updateCallback += UpdateTerrainChunk;
                if (i == colliderLODIndex)
                {
                    _lodMeshes[i].updateCallback += UpdateCollisionMesh;
                }
            }

            _mapGenerator.RequestMapData(_position, OnMapDataReceived);
        }

        private void OnMapDataReceived(MapData mapData)
        {
            _mapData = mapData;
            _mapDataReceived = true;

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

                bool wasVisible = IsVisible();
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
                        {
                            _previousLODIndex = lodIndex;
                            _meshFilter.mesh = lodMesh.mesh;
                        }
                        else if (!lodMesh.hasRequestedMesh)
                            lodMesh.RequestMesh(_mapData);
                    }
                }

                if (wasVisible != visible)
                    if (visible)
                        _visibleTerrainChunks.Add(this);
                    else
                        _visibleTerrainChunks.Remove(this);

                SetVisible(visible);
            }
        }

        public void UpdateCollisionMesh()
        {
            if (_hasSetCollider)
                return;

            float sqrDstFromViewerToEdge = _bounds.SqrDistance(viewerPosition);

            if (sqrDstFromViewerToEdge < _detailLevels[_colliderLODIndex].sqrVisibleDstThreshold)
            {
                if (!_lodMeshes[_colliderLODIndex].hasRequestedMesh)
                    _lodMeshes[_colliderLODIndex].RequestMesh(_mapData);
            }

            if (sqrDstFromViewerToEdge < ColliderGenerationDstThreshold * ColliderGenerationDstThreshold)
            {
                if (_lodMeshes[_colliderLODIndex].hasMesh)
                {
                    _meshCollider.sharedMesh = _lodMeshes[_colliderLODIndex].mesh;
                    _hasSetCollider = true;
                }
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
        public event Action updateCallback;

        private int _lod;

        public LODMesh(int lod)
        {
            _lod = lod;
        }

        private void OnMeshDataReceived(MeshData meshData)
        {
            mesh = meshData.CreateMesh();
            hasMesh = true;
            updateCallback();
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
        [Range(0, MeshGenerator.numSupportedLODs - 1)]
        public int lod;
        public float visibleDstThreshold;

        public float sqrVisibleDstThreshold
        {
            get { return visibleDstThreshold * visibleDstThreshold; }
        }
    }
}