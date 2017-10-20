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
    private Vector2 viewerPositionOld;

    private static MapGenerator mapGenerator;
    private float meshWorldSize;
    private int chunksVisibleInViewDst;

    private Dictionary<Vector2, TerrainChunk> _terrainChunkDictionary = new Dictionary<Vector2, TerrainChunk>();
    private static List<TerrainChunk> _visibleTerrainChunks = new List<TerrainChunk>();

    void Start()
    {
        maxViewDst = detailLevels[detailLevels.Length - 1].visibleDstThreshold;
        mapGenerator = FindObjectOfType<MapGenerator>();
        meshWorldSize = mapGenerator.meshSettings.meshWorldSize;
        chunksVisibleInViewDst = Mathf.RoundToInt(maxViewDst / meshWorldSize);

        UpdateVisibleChunks();
    }

    void Update()
    {
        viewerPosition = new Vector2(viewer.position.x, viewer.position.z) / mapGenerator.meshSettings.meshScale;

        if (viewerPosition != viewerPositionOld)
        {
            foreach (TerrainChunk chunk in _visibleTerrainChunks)
            {
                chunk.UpdateCollisionMesh();
            }
        }

        if ((viewerPositionOld - viewerPosition).sqrMagnitude > SqrViewerMoveThresholdForChunkUpdate)
        {
            viewerPositionOld = viewerPosition;
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

        int currentChunkCoordX = Mathf.RoundToInt(viewerPosition.x / meshWorldSize);
        int currentChunkCoordY = Mathf.RoundToInt(viewerPosition.y / meshWorldSize);

        for (int yOffset = -chunksVisibleInViewDst; yOffset <= chunksVisibleInViewDst; yOffset++)
        {
            for (int xOffset = -chunksVisibleInViewDst; xOffset <= chunksVisibleInViewDst; xOffset++)
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
                            new TerrainChunk(viewedChunkCoord, meshWorldSize, detailLevels, colliderLODIndex, transform,
                                mapMaterial));
                }
            }
        }
    }


    public class TerrainChunk
    {
        public Vector2 coordinate;

        private GameObject meshObject;
        private Vector2 sampleCenter;
        private Bounds bounds;

        private MeshRenderer meshRenderer;
        private MeshFilter meshFilter;
        private MeshCollider meshCollider;

        private LODInfo[] detailLevels;
        private LODMesh[] lodMeshes;
        private int colliderLODIndex;

        private HeightMap _heightMap;
        private bool mapDataReceived;
        private int previousLODIndex = -1;
        private bool hasSetCollider;

        public TerrainChunk(Vector2 coord, float meshWorldSize, LODInfo[] detailLevels, int colliderLODIndex, Transform parent,
            Material material)
        {
            coordinate = coord;
            this.detailLevels = detailLevels;
            this.colliderLODIndex = colliderLODIndex;
            sampleCenter = coord * meshWorldSize / mapGenerator.meshSettings.meshScale;
            Vector2 position = coord * meshWorldSize;
            bounds = new Bounds(sampleCenter, Vector2.one * meshWorldSize);

            meshObject = new GameObject("Terrain Chunk");
            meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshFilter = meshObject.AddComponent<MeshFilter>();
            meshCollider = meshObject.AddComponent<MeshCollider>();

            meshRenderer.material = material;
            meshObject.transform.position = new Vector3(position.x, 0 , position.y);
            meshObject.transform.parent = parent;
            SetVisible(false);

            lodMeshes = new LODMesh[detailLevels.Length];
            for (int i = 0; i < detailLevels.Length; i++)
            {
                lodMeshes[i] = new LODMesh(detailLevels[i].lod);
                lodMeshes[i].updateCallback += UpdateTerrainChunk;
                if (i == colliderLODIndex)
                {
                    lodMeshes[i].updateCallback += UpdateCollisionMesh;
                }
            }

            mapGenerator.RequestHeightMap(sampleCenter, OnMapDataReceived);
        }

        private void OnMapDataReceived(HeightMap heightMap)
        {
            _heightMap = heightMap;
            mapDataReceived = true;

            UpdateTerrainChunk();
        }

        void OnMeshDataReceived(MeshData meshData)
        {
            meshFilter.mesh = meshData.CreateMesh();
        }

        public void UpdateTerrainChunk()
        {

            if (mapDataReceived)
            {
                float viewerDstFromNearestEdge = Mathf.Sqrt(bounds.SqrDistance(viewerPosition));

                bool wasVisible = IsVisible();
                bool visible = viewerDstFromNearestEdge <= maxViewDst;

                if (visible)
                {
                    int lodIndex = 0;
                    for (int i = 0; i < detailLevels.Length - 1; i++)
                    {
                        if (viewerDstFromNearestEdge > detailLevels[i].visibleDstThreshold)
                            lodIndex = i + 1;
                        else
                            break;
                    }

                    if (lodIndex != previousLODIndex)
                    {
                        LODMesh lodMesh = lodMeshes[lodIndex];
                        if (lodMesh.hasMesh)
                        {
                            previousLODIndex = lodIndex;
                            meshFilter.mesh = lodMesh.mesh;
                        }
                        else if (!lodMesh.hasRequestedMesh)
                            lodMesh.RequestMesh(_heightMap);
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
            if (hasSetCollider)
                return;

            float sqrDstFromViewerToEdge = bounds.SqrDistance(viewerPosition);

            if (sqrDstFromViewerToEdge < detailLevels[colliderLODIndex].sqrVisibleDstThreshold)
            {
                if (!lodMeshes[colliderLODIndex].hasRequestedMesh)
                    lodMeshes[colliderLODIndex].RequestMesh(_heightMap);
            }

            if (sqrDstFromViewerToEdge < ColliderGenerationDstThreshold * ColliderGenerationDstThreshold)
            {
                if (lodMeshes[colliderLODIndex].hasMesh)
                {
                    meshCollider.sharedMesh = lodMeshes[colliderLODIndex].mesh;
                    hasSetCollider = true;
                }
            }
        }

        public void SetVisible(bool visible)
        {
            meshObject.SetActive(visible);
        }

        public bool IsVisible()
        {
            return meshObject.activeSelf;
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

        public void RequestMesh(HeightMap heightMap)
        {
            hasRequestedMesh = true;
            mapGenerator.RequestMeshData(heightMap, _lod, OnMeshDataReceived);
        }
    }

    [Serializable]
    public struct LODInfo
    {
        [Range(0, MeshSettings.numSupportedLODs - 1)]
        public int lod;
        public float visibleDstThreshold;

        public float sqrVisibleDstThreshold
        {
            get { return visibleDstThreshold * visibleDstThreshold; }
        }
    }
}