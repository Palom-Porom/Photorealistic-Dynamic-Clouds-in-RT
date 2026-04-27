using UnityEngine;
using UnityEditor;
using System.IO;

public class TgaToVolumeTextureDirect : EditorWindow
{
    [MenuItem("Tools/Create 3D Texture from TGA Atlas (Direct Read)")]
    static void CreateVolumeTextureDirect()
    {
        string path = EditorUtility.OpenFilePanel("Select TGA Atlas", Application.dataPath, "tga");
        if (string.IsNullOrEmpty(path)) return;

        byte[] tgaData = File.ReadAllBytes(path);
        if (tgaData.Length < 18)
        {
            Debug.LogError("Invalid TGA file.");
            return;
        }

        // --- Парсинг заголовка TGA ---
        int idLength = tgaData[0];
        int colorMapType = tgaData[1];
        int imageType = tgaData[2];
        int width = tgaData[12] | (tgaData[13] << 8);
        int height = tgaData[14] | (tgaData[15] << 8);
        int pixelDepth = tgaData[16];
        int imageDescriptor = tgaData[17];

        // Поддерживаем только несжатые 32-битные truecolor (тип 2)
        if (imageType != 2 || pixelDepth != 32)
        {
            Debug.LogError($"Поддерживаются только несжатые 32-битные TGA. Тип: {imageType}, глубина: {pixelDepth}");
            return;
        }

        Debug.Log($"TGA размер: {width} x {height}");

        // --- Вычисляем начало пиксельных данных ---
        int dataOffset = 18 + idLength;
        if (colorMapType == 1)
        {
            int cmapLength = tgaData[5] | (tgaData[6] << 8);
            int cmapEntrySize = tgaData[7];
            int cmapBytes = cmapLength * (cmapEntrySize / 8);
            dataOffset += cmapBytes;
        }

        int expectedDataSize = width * height * 4;
        if (tgaData.Length - dataOffset != expectedDataSize)
        {
            Debug.LogError($"Неверный размер данных. Ожидалось {expectedDataSize}, получено {tgaData.Length - dataOffset}");
            return;
        }

        // --- Проверяем структуру атласа ---
        int size = height; // для атласа 16384x128 размер куба = 128
        if (width != size * size)
        {
            Debug.LogError($"Атлас имеет неверный формат: ширина {width} != {size}*{size}={size * size}");
            return;
        }

        // --- Создаём 3D текстуру ---
        Texture3D volume = new Texture3D(size, size, size, TextureFormat.RGBA32, false);
        volume.filterMode = FilterMode.Bilinear;
        volume.wrapMode = TextureWrapMode.Repeat;

        Color[] volumePixels = new Color[size * size * size];

        for (int z = 0; z < size; z++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int atlasX = x + z * size;
                    int atlasY = y;
                    int pixelIndex = (atlasY * width + atlasX) * 4;

                    // TGA хранит пиксели в порядке B, G, R, A
                    byte b = tgaData[dataOffset + pixelIndex];
                    byte g = tgaData[dataOffset + pixelIndex + 1];
                    byte r = tgaData[dataOffset + pixelIndex + 2];
                    byte a = tgaData[dataOffset + pixelIndex + 3];

                    volumePixels[x + y * size + z * size * size] = new Color32(r, g, b, a);
                }
            }
        }

        volume.SetPixels(volumePixels);
        volume.Apply();

        // --- Сохраняем как .asset ---
        string savePath = EditorUtility.SaveFilePanelInProject("Save 3D Texture", "CloudBaseShape", "asset", "");
        if (!string.IsNullOrEmpty(savePath))
        {
            AssetDatabase.CreateAsset(volume, savePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"3D текстура сохранена: {savePath}");
        }
    }
}