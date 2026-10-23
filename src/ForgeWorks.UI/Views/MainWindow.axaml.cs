using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using ForgeWorks.Core.Geometry;
using ForgeWorks.UI.ViewModels;

namespace ForgeWorks.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.RequestOpenTagEditorWindow += () =>
            {
                var win = new TagEditorWindow { DataContext = vm };
                win.Show(this);
            };

            vm.RequestImportFileDialog += async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Custom Geometry (All Supported Formats)",
                    AllowMultiple = true,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("All Supported 3D Formats")
                        {
                            Patterns = new[] { "*.obj", "*.fbx", "*.gltf", "*.glb", "*.ass", "*.jms", "*.rmf", "*.map", "*.dae", "*.stl", "*.ply", "*.3ds" }
                        },
                        new FilePickerFileType("Wavefront OBJ (*.obj)") { Patterns = new[] { "*.obj" } },
                        new FilePickerFileType("Autodesk FBX (*.fbx)") { Patterns = new[] { "*.fbx" } },
                        new FilePickerFileType("glTF / GLB (*.gltf, *.glb)") { Patterns = new[] { "*.gltf", "*.glb" } },
                        new FilePickerFileType("Bungie Amalgam Scene (*.ass)") { Patterns = new[] { "*.ass" } },
                        new FilePickerFileType("Halo Jointed Model (*.jms)") { Patterns = new[] { "*.jms" } },
                        new FilePickerFileType("Valve Hammer / Worldcraft (*.rmf, *.map)") { Patterns = new[] { "*.rmf", "*.map" } },
                        new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*.*" } }
                    }
                });

                if (files != null && files.Count > 0)
                {
                    foreach (var file in files)
                    {
                        string? localPath = file.TryGetLocalPath();
                        if (!string.IsNullOrEmpty(localPath))
                        {
                            vm.ImportGeometryFile(localPath);
                        }
                    }
                }
            };

            vm.RequestImportTexturesDialog += async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Model PNG / JPG / TIF Textures & Generate Custom Halo 3 Shaders",
                    AllowMultiple = true,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Image Textures (*.png, *.jpg, *.jpeg, *.tif, *.tiff, *.bmp)")
                        {
                            Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.tif", "*.tiff", "*.bmp" }
                        },
                        new FilePickerFileType("PNG Textures (*.png)") { Patterns = new[] { "*.png" } },
                        new FilePickerFileType("TIFF Textures (*.tif, *.tiff)") { Patterns = new[] { "*.tif", "*.tiff" } },
                        new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*.*" } }
                    }
                });

                if (files != null && files.Count > 0)
                {
                    string? first = files[0].TryGetLocalPath();
                    if (!string.IsNullOrEmpty(first))
                    {
                        string dir = System.IO.Path.GetDirectoryName(first) ?? "";
                        vm.ImportModelTextures(dir);
                    }
                }
            };
        }
    }

    public void OnOutlinerNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control c && c.DataContext is SceneNode node && DataContext is MainViewModel vm)
        {
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            vm.HandleNodeClicked(node, shift, ctrl);
        }
    }
}