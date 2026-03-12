// using UnityEngine;
// using UnityEditor;
// using System.IO;
//
// public class Texture3DImporter
// {
//     private const int SIZE = 32;
//
//     [MenuItem("Tools/Import Cloud 3D Noise")]
//     static void ImportTexture3D()
//     {
//         string path = EditorUtility.OpenFilePanel(
//             "Select RAW 3D Noise File",
//             "",
//             "raw"
//         );
//
//         if (string.IsNullOrEmpty(path))
//             return;
//
//         byte[] bytes = File.ReadAllBytes(path);
//
//         int voxelCount = SIZE * SIZE * SIZE;
//         int expectedFloatCount = voxelCount * 4; // RGBA
//         int expectedByteCount = expectedFloatCount * sizeof(float);
//
//         if (bytes.Length != expectedByteCount)
//         {
//             Debug.LogError($"Wrong file size. Expected {expectedByteCount} bytes.");
//             return;
//         }
//
//         float[] floatData = new float[expectedFloatCount];
//         System.Buffer.BlockCopy(bytes, 0, floatData, 0, bytes.Length);
//
//         Texture3D texture = new Texture3D(
//             SIZE,
//             SIZE,
//             SIZE,
//             TextureFormat.RGBAFloat,
//             false
//         );
//
//         texture.wrapMode = TextureWrapMode.Repeat;
//         texture.filterMode = FilterMode.Trilinear;
//
//         Color[] colors = new Color[voxelCount];
//
//         for (int i = 0; i < voxelCount; i++)
//         {
//             int idx = i * 4;
//
//             colors[i] = new Color(
//                 floatData[idx + 0],
//                 floatData[idx + 1],
//                 floatData[idx + 2],
//                 floatData[idx + 3]
//             );
//         }
//
//         texture.SetPixels(colors);
//         texture.Apply();
//
//         string assetPath = "Assets/CloudDetailNoise_32.asset";
//         AssetDatabase.CreateAsset(texture, assetPath);
//         AssetDatabase.SaveAssets();
//
//         Debug.Log("3D texture imported successfully!");
//     }
// }


using UnityEngine;
using UnityEditor;
using System.IO;

public class Texture2DImporter
{
    private const int WIDTH = 128;
    private const int HEIGHT = 128;
    private const int CHANNELS = 3; // RGB

    [MenuItem("Tools/Import 2D Curl Noise")]
    static void ImportTexture2D()
    {
        string path = EditorUtility.OpenFilePanel(
            "Select RAW Curl Noise File (3 channels, 128x128, float)",
            "",
            "raw"
        );

        if (string.IsNullOrEmpty(path))
            return;

        byte[] bytes = File.ReadAllBytes(path);

        int pixelCount = WIDTH * HEIGHT;
        int expectedFloatCount = pixelCount * CHANNELS;
        int expectedByteCount = expectedFloatCount * sizeof(float);

        if (bytes.Length != expectedByteCount)
        {
            Debug.LogError($"Wrong file size. Expected {expectedByteCount} bytes, got {bytes.Length}.");
            return;
        }

        float[] floatData = new float[expectedFloatCount];
        System.Buffer.BlockCopy(bytes, 0, floatData, 0, bytes.Length);

        // Создаём текстуру с поддержкой float (RGBAFloat)
        Texture2D texture = new Texture2D(
            WIDTH,
            HEIGHT,
            TextureFormat.RGBAFloat,
            false
        );

        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;

        Color[] colors = new Color[pixelCount];

        for (int i = 0; i < pixelCount; i++)
        {
            int idx = i * CHANNELS; // 3 канала в файле

            colors[i] = new Color(
                floatData[idx + 0],
                floatData[idx + 1],
                floatData[idx + 2],
                1f // альфа = 1
            );
        }

        texture.SetPixels(colors);
        texture.Apply();

        string assetPath = "Assets/CurlNoise_128_RGB.asset";
        AssetDatabase.CreateAsset(texture, assetPath);
        AssetDatabase.SaveAssets();

        Debug.Log("2D curl noise texture imported successfully!");
    }
}