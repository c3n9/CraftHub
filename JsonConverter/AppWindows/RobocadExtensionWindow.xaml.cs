using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace JsonConverter.AppWindows
{
    /// <summary>
    /// Логика взаимодействия для RobocadExtensionWindow.xaml
    /// </summary>
    public partial class RobocadExtensionWindow : Window
    {
        public RobocadExtensionWindow()
        {
            InitializeComponent();
        }
        private void BGenerate_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "*.png, *.jpg;|*.png; *.jpg;";

            if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedImagePath = openFileDialog.FileName;
                var folderBrowserDialog = new FolderBrowserDialog();
                if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string selectedPath = folderBrowserDialog.SelectedPath;
                    for (int i = 1; i <= 3; i++)
                    {
                        string folderName = $"{i} folder";
                        string folderPath = System.IO.Path.Combine(selectedPath, folderName);

                        // Проверяем, существует ли папка, прежде чем создавать
                        if (!Directory.Exists(folderPath))
                        {
                            Directory.CreateDirectory(folderPath);
                            // Копирование изображения в созданную папку
                            string destinationImagePath = System.IO.Path.Combine(folderPath, System.IO.Path.GetFileName(selectedImagePath));
                            File.Copy(selectedImagePath, destinationImagePath);

                            // Добавление метаинформации для Unity (пример)
                            string metaFilePath = System.IO.Path.Combine(folderPath, $"{System.IO.Path.GetFileName(selectedImagePath)}.meta");
                            File.WriteAllText(metaFilePath,  $"fileFormatVersion: 2\r\n" +
                                $"guid: 98f2f9897b08b664db1c82ad3c1219d1\r\n" +
                                $"TextureImporter:\r\n  internalIDToNameTable: []\r\n  " +
                                $"externalObjects: {{}}\r\n  serializedVersion: 12\r\n  " +
                                $"mipmaps:\r\n    mipMapMode: 0\r\n    enableMipMap: 0\r\n    " +
                                $"sRGBTexture: 1\r\n    linearTexture: 0\r\n    fadeOut: 0\r\n    " +
                                $"borderMipMap: 0\r\n    mipMapsPreserveCoverage: 0\r\n    " +
                                $"alphaTestReferenceValue: 0.5\r\n    mipMapFadeDistanceStart: 1\r\n    " +
                                $"mipMapFadeDistanceEnd: 3\r\n  bumpmap:\r\n    convertToNormalMap: 0\r\n    " +
                                $"externalNormalMap: 0\r\n    heightScale: 0.25\r\n    normalMapFilter: 0\r\n  " +
                                $"isReadable: 0\r\n  streamingMipmaps: 0\r\n  streamingMipmapsPriority: 0\r\n  " +
                                $"vTOnly: 0\r\n  ignoreMasterTextureLimit: 0\r\n  grayScaleToAlpha: 0\r\n  " +
                                $"generateCubemap: 6\r\n  cubemapConvolution: 0\r\n  seamlessCubemap: 0\r\n  " +
                                $"textureFormat: 1\r\n  maxTextureSize: 2048\r\n  textureSettings:\r\n    " +
                                $"serializedVersion: 2\r\n    filterMode: 1\r\n    aniso: 1\r\n    " +
                                $"mipBias: 0\r\n    wrapU: 1\r\n    wrapV: 1\r\n    wrapW: 0\r\n  " +
                                $"nPOTScale: 0\r\n  lightmap: 0\r\n  compressionQuality: 50\r\n  " +
                                $"spriteMode: 1\r\n  spriteExtrude: 1\r\n  spriteMeshType: 1\r\n  " +
                                $"alignment: 0\r\n  spritePivot: {{x: 0.5, y: 0.5}}\r\n  spritePixelsToUnits: 100\r\n  " +
                                $"spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}\r\n  spriteGenerateFallbackPhysicsShape: 1\r\n  " +
                                $"alphaUsage: 1\r\n  alphaIsTransparency: 1\r\n  spriteTessellationDetail: -1\r\n  textureType: 8\r\n  " +
                                $"textureShape: 1\r\n  singleChannelComponent: 0\r\n  flipbookRows: 1\r\n  flipbookColumns: 1\r\n  " +
                                $"maxTextureSizeSet: 0\r\n  compressionQualitySet: 0\r\n  textureFormatSet: 0\r\n  ignorePngGamma: 0\r\n " +
                                $" applyGammaDecoding: 0\r\n  cookieLightType: 0\r\n  platformSettings:\r\n  - serializedVersion: 3\r\n    " +
                                $"buildTarget: DefaultTexturePlatform\r\n    maxTextureSize: 2048\r\n    resizeAlgorithm: 0\r\n    " +
                                $"textureFormat: -1\r\n    textureCompression: 1\r\n    compressionQuality: 50\r\n    " +
                                $"crunchedCompression: 0\r\n    allowsAlphaSplitting: 0\r\n    overridden: 0\r\n    " +
                                $"androidETC2FallbackOverride: 0\r\n    forceMaximumCompressionQuality_BC6H_BC7: 0\r\n  " +
                                $"- serializedVersion: 3\r\n    buildTarget: Standalone\r\n    maxTextureSize: 2048\r\n    " +
                                $"resizeAlgorithm: 0\r\n    textureFormat: -1\r\n    textureCompression: 1\r\n    " +
                                $"compressionQuality: 50\r\n    crunchedCompression: 0\r\n    allowsAlphaSplitting: 0\r\n    " +
                                $"overridden: 0\r\n    androidETC2FallbackOverride: 0\r\n    forceMaximumCompressionQuality_BC6H_BC7: 0\r\n  " +
                                $"- serializedVersion: 3\r\n    buildTarget: Server\r\n    maxTextureSize: 2048\r\n    resizeAlgorithm: 0\r\n    " +
                                $"textureFormat: -1\r\n    textureCompression: 1\r\n    compressionQuality: 50\r\n    crunchedCompression: 0\r\n    " +
                                $"allowsAlphaSplitting: 0\r\n    overridden: 0\r\n    androidETC2FallbackOverride: 0\r\n    forceMaximumCompressionQuality_BC6H_BC7: 0\r\n  " +
                                $"spriteSheet:\r\n    serializedVersion: 2\r\n    sprites: []\r\n    outline: []\r\n    physicsShape: []\r\n    bones: []\r\n    " +
                                $"spriteID: 5e97eb03825dee720800000000000000\r\n    internalID: 0\r\n    vertices: []\r\n    indices: \r\n    edges: []\r\n    weights: []\r\n    " +
                                $"secondaryTextures: []\r\n    nameFileIdTable: {{}}\r\n  spritePackingTag: \r\n  pSDRemoveMatte: 0\r\n  pSDShowRemoveMatteOption: 0\r\n  " +
                                $"userData: \r\n  assetBundleName: \r\n  assetBundleVariant: \r\n");
                        }
                    }
                }
            }
        }
    }
}
