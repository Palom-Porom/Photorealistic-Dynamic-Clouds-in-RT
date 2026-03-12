using UnityEngine;
using UnityEditor;
using System.IO;

public class Texture3DImporter
{
    private const int SIZE = 128;

    [MenuItem("Tools/Import Cloud 3D Noise")]
    static void ImportTexture3D()
    {
        string path = EditorUtility.OpenFilePanel(
            "Select RAW 3D Noise File",
            "",
            "raw"
        );

        if (string.IsNullOrEmpty(path))
            return;

        byte[] bytes = File.ReadAllBytes(path);

        int voxelCount = SIZE * SIZE * SIZE;
        int expectedFloatCount = voxelCount * 4; // RGBA
        int expectedByteCount = expectedFloatCount * sizeof(float);

        if (bytes.Length != expectedByteCount)
        {
            Debug.LogError($"Wrong file size. Expected {expectedByteCount} bytes.");
            return;
        }

        float[] floatData = new float[expectedFloatCount];
        System.Buffer.BlockCopy(bytes, 0, floatData, 0, bytes.Length);

        Texture3D texture = new Texture3D(
            SIZE,
            SIZE,
            SIZE,
            TextureFormat.RGBAFloat,
            false
        );

        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Trilinear;

        Color[] colors = new Color[voxelCount];

        for (int i = 0; i < voxelCount; i++)
        {
            int idx = i * 4;

            colors[i] = new Color(
                floatData[idx + 0],
                floatData[idx + 1],
                floatData[idx + 2],
                floatData[idx + 3]
            );
        }

        texture.SetPixels(colors);
        texture.Apply();

        string assetPath = "Assets/CloudBaseNoise_128.asset";
        AssetDatabase.CreateAsset(texture, assetPath);
        AssetDatabase.SaveAssets();

        Debug.Log("3D texture imported successfully!");
    }
}