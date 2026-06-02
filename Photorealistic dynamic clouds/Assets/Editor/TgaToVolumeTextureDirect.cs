using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class RawToVolumeTextureDirect : EditorWindow
{
    [MenuItem("Tools/Create 3D Texture from RAW (Manual Mips)")]
    static void CreateVolumeTextureDirectFromRaw()
    {
        // Изменяем фильтр на выбор файлов .raw
        string path = EditorUtility.OpenFilePanel("Select RAW 3D Texture", Application.dataPath, "raw");
        if (string.IsNullOrEmpty(path)) return;

        byte[] rawData = File.ReadAllBytes(path);

        // --- Задаем параметры куба 256х256х256 ---
        int size = 256; 
        int channels = 4; // RGBA
        int expectedDataSize = size * size * size * channels; // 67 108 864 байт (64 МБ)

        // Безопасная проверка на соответствие размера файла
        if (rawData.Length != expectedDataSize)
        {
            Debug.LogError($"Неверный размер RAW файла. Ожидалось {expectedDataSize} байт для {size}³, получено {rawData.Length} байт.");
            return;
        }

        Debug.Log($"RAW файл успешно прочитан. Размер структуры: {size} x {size} x {size}");

        // --- Создаём 3D текстуру с поддержкой мип-уровней ---
        Texture3D volume = new Texture3D(size, size, size, TextureFormat.RGBA32, true); // mipChain = true
        volume.filterMode = FilterMode.Trilinear; // Трилинейная фильтрация для плавных переходов LOD в HDRP
        volume.wrapMode = TextureWrapMode.Repeat; //

        // Определяем количество мип-уровней (для размера 256 это будет 9 уровней)
        int mipCount = Mathf.FloorToInt(Mathf.Log(size, 2)) + 1; //
        Debug.Log($"Создаётся 3D текстура {size}³ с {mipCount} мип-уровнями"); //

        // Массив массивов пикселей для каждого мип-уровня
        List<Color[]> mipPixels = new List<Color[]>(mipCount); //

        // --- Заполнение мип-уровня 0 (исходные данные из RAW) ---
        Color[] level0Pixels = new Color[size * size * size]; //
        
        // Читаем напрямую из трехмерного пространства RAW (безбарьерный линейный обход)
        for (int z = 0; z < size; z++) //
        {
            for (int y = 0; y < size; y++) //
            {
                for (int x = 0; x < size; x++) //
                {
                    // Вычисляем индекс в плоском байтовом массиве RAW
                    int pixelIndex = (x + y * size + z * size * size) * 4;

                    // Извлекаем каналы в правильном порядке RGBA (как они были записаны в C++)
                    byte r = rawData[pixelIndex];
                    byte g = rawData[pixelIndex + 1];
                    byte b = rawData[pixelIndex + 2];
                    byte a = rawData[pixelIndex + 3];

                    level0Pixels[x + y * size + z * size * size] = new Color32(r, g, b, a); //
                }
            }
        }
        mipPixels.Add(level0Pixels); //

        // --- Ваша оригинальная генерация остальных мип-уровней (даунсэмплинг 2x2x2) ---
        // Этот блок полностью сохранен, так как он идеально работает с массивом структур Color в памяти
        int prevSize = size; //
        Color[] prevPixels = level0Pixels; //

        for (int level = 1; level < mipCount; level++) //
        {
            int curSize = Mathf.Max(1, prevSize / 2); //
            Color[] curPixels = new Color[curSize * curSize * curSize]; //

            for (int z = 0; z < curSize; z++) //
            {
                for (int y = 0; y < curSize; y++) //
                {
                    for (int x = 0; x < curSize; x++) //
                    {
                        // Координаты в предыдущем мип-уровне (2x2x2 блок)
                        int x0 = x * 2; //
                        int y0 = y * 2; //
                        int z0 = z * 2; //

                        // Усредняем 8 соседних вокселей
                        Color sum = Color.clear; //
                        for (int dz = 0; dz < 2; dz++) //
                        {
                            for (int dy = 0; dy < 2; dy++) //
                            {
                                for (int dx = 0; dx < 2; dx++) //
                                {
                                    int nx = x0 + dx; //
                                    int ny = y0 + dy; //
                                    int nz = z0 + dz; //
                                    if (nx < prevSize && ny < prevSize && nz < prevSize) //
                                    {
                                        sum += prevPixels[nx + ny * prevSize + nz * prevSize * prevSize]; //
                                    }
                                }
                            }
                        }
                        curPixels[x + y * curSize + z * curSize * curSize] = sum / 8.0f; //
                    }
                }
            }

            mipPixels.Add(curPixels); //
            prevPixels = curPixels; //
            prevSize = curSize; //
        }

        // --- Записываем все мип-уровни в текстуру ---
        for (int level = 0; level < mipCount; level++) //
        {
            volume.SetPixels(mipPixels[level], level); //
        }

        volume.Apply(false, false); // Изменено на false во втором параметре (мы вручную передали все мипы, Unity не нужно их пересчитывать стандартными методами)

        // --- Сохраняем как .asset ---
        string savePath = EditorUtility.SaveFilePanelInProject("Save 3D Texture", "CloudBaseShape256", "asset", ""); //
        if (!string.IsNullOrEmpty(savePath)) //
        {
            AssetDatabase.CreateAsset(volume, savePath); //
            AssetDatabase.SaveAssets(); //
            Debug.Log($"3D RAW текстура сохранена: {savePath} (мип-уровни: {mipCount})"); //
        }
    }
}   