using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace ForgeWorks.Blam.Tags;

public sealed class TagFolderNode
{
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; } = true;
    public string TagGroup { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public BlamTagFileEntry? TagFile { get; set; }

    public ObservableCollection<TagFolderNode> Children { get; } = new();

    public string Icon => IsDirectory ? "📁" : TagGroup switch
    {
        "scnr" => "🌐",
        "mode" or "hlmt" => "🗿",
        "coll" => "📦",
        "rmsh" or "rmen" or "rmtr" => "🎨",
        "bitm" => "🖼️",
        "weap" => "🔫",
        "vehi" => "🚗",
        "scen" => "🏰",
        "bipd" => "🏃",
        "snd!" => "🔊",
        _ => "📄"
    };
}

public sealed class TagHierarchyBuilder
{
    public static TagFolderNode BuildTagTree(string? ekPath, string searchQuery = "", string filterGroup = "all")
    {
        var root = new TagFolderNode
        {
            Name = "tags",
            RelativePath = "tags",
            IsDirectory = true
        };

        var allTags = TagBrowserService.DiscoverTags(ekPath, filterGroup, searchQuery);

        foreach (var tag in allTags)
        {
            string rel = tag.RelativePath.Replace('/', '\\');
            var parts = rel.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            var current = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string folderName = parts[i];
                var existing = current.Children.FirstOrDefault(c => c.IsDirectory && c.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    existing = new TagFolderNode
                    {
                        Name = folderName,
                        RelativePath = Path.Combine(current.RelativePath, folderName),
                        IsDirectory = true
                    };
                    current.Children.Add(existing);
                }
                current = existing;
            }

            // Leaf file
            string fileName = parts[^1];
            current.Children.Add(new TagFolderNode
            {
                Name = fileName,
                RelativePath = tag.RelativePath,
                IsDirectory = false,
                TagGroup = tag.TagGroup,
                FileSizeBytes = tag.FileSizeBytes,
                TagFile = tag
            });
        }

        return root;
    }
}
