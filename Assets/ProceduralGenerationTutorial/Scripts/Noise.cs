using UnityEngine;

public static class Noise  {

    public static float[,] GenerateNoiseMap(int mapWidth, int mapHeight, float scale, int octaves, float persistance, float lacunarity)
    {
        float[,] noiseMap = new float[mapWidth, mapHeight];

        if (scale <= 0.0001f)
            scale = 0.0001f;

        for(int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                float amplitude = 1;
                float frequency = 1;
                float noiseHeight = 0;

                for(int i = 0; i < octaves; i++)
                { 
                    float sampleX = x / scale * frequency;
                    float sampleY = y / scale * frequency;

                    float perlinValue = Mathf.PerlinNoise(sampleX, sampleY) * 2 - 1;
                    noiseHeight += perlinValue * amplitude;

                    amplitude *= persistance;
                    frequency *= lacunarity;
                }
                noiseMap[x, y] = noiseHeight;
            }
        }

        return noiseMap;
    }
     
}
