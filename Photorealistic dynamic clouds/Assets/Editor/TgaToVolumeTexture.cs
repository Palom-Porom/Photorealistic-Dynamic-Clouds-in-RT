using UnityEngine;
using UnityEditor;
using System.IO;

public class TgaToVolumeTexture : EditorWindow
{
    [MenuItem("Tools/Create 3D Texture from TGA Atlas")]
    static void CreateVolumeTexture()
    {
        // Выбор исходного TGA файла
        string path = EditorUtility.OpenFilePanel("Select TGA Atlas", Application.dataPath, "tga");
        if (string.IsNullOrEmpty(path)) return;

        // Получаем путь относительно Assets
        string assetPath = "Assets" + path.Substring(Application.dataPath.Length);
        Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (source == null)
        {
            Debug.LogError("Не удалось загрузить текстуру. Убедитесь, что файл находится внутри папки Assets.");
            return;
        }

        // Включаем возможность чтения пикселей
        string texturePath = AssetDatabase.GetAssetPath(source);
        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer != null && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        // Проверяем размеры
        int width = source.width;
        int height = source.height;

        Debug.Log($"Исходная текстура: {width} x {height}");

        // Определяем размер 3D текстуры (предполагаем, что высота = размер куба)
        int size = height;
        if (width != size * size)
        {
            Debug.LogError($"Неверный формат атласа: ожидалась ширина {size}*{size}={size*size}, получена {width}. " +
                           "Проверьте настройки импорта текстуры (отключите изменение размера NPOT).");
            return;
        }

        // Создаём 3D текстуру
        TextureFormat format = TextureFormat.RGBA32;
        Texture3D volume = new Texture3D(size, size, size, format, false);
        volume.filterMode = FilterMode.Bilinear;
        volume.wrapMode = TextureWrapMode.Repeat;

        Color[] pixels = source.GetPixels();
        Color[] volumePixels = new Color[size * size * size];

        for (int z = 0; z < size; z++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Координаты в атласе: X сдвигается на size для каждого слоя Z
                    int atlasX = x + z * size;
                    int atlasY = y;
                    int index = atlasY * width + atlasX;

                    // Защита от выхода за границы (на всякий случай)
                    if (index < 0 || index >= pixels.Length)
                    {
                        Debug.LogError($"Индекс {index} вне диапазона [0, {pixels.Length})");
                        return;
                    }

                    // Индекс в 3D массиве
                    int volumeIndex = x + y * size + z * size * size;
                    volumePixels[volumeIndex] = pixels[index];
                }
            }
        }

        volume.SetPixels(volumePixels);
        volume.Apply();

        // Сохраняем как .asset
        string savePath = EditorUtility.SaveFilePanelInProject("Save 3D Texture", "VolumeTexture", "asset", "");
        if (!string.IsNullOrEmpty(savePath))
        {
            AssetDatabase.CreateAsset(volume, savePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"3D текстура сохранена: {savePath}");
        }
    }
}