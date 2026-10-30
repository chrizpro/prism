using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;

namespace Prism.ViewModels;

/// <summary>The Part info section of the Properties panel: what the part is (number, material, supplier, tags), saved in its file.</summary>
public sealed partial class MainViewModel
{
    [ObservableProperty] public partial string PartNumber { get; set; } = "";
    [ObservableProperty] public partial string PartDescription { get; set; } = "";
    [ObservableProperty] public partial string PartRevision { get; set; } = "";
    [ObservableProperty] public partial string PartMaterial { get; set; } = "";
    [ObservableProperty] public partial string PartSupplier { get; set; } = "";
    [ObservableProperty] public partial string PartCost { get; set; } = "";
    [ObservableProperty] public partial string PartTags { get; set; } = "";
    [ObservableProperty] public partial string PartCustom { get; set; } = "";
    bool _syncingPartInfo;

    partial void OnPartNumberChanged(string value) => PushPartInfo();
    partial void OnPartDescriptionChanged(string value) => PushPartInfo();
    partial void OnPartRevisionChanged(string value) => PushPartInfo();
    partial void OnPartMaterialChanged(string value) => PushPartInfo();
    partial void OnPartSupplierChanged(string value) => PushPartInfo();
    partial void OnPartCostChanged(string value) => PushPartInfo();
    partial void OnPartTagsChanged(string value) => PushPartInfo();
    partial void OnPartCustomChanged(string value) => PushPartInfo();

    /// <summary>Show the document's part info in the panel.</summary>
    void SyncPartInfo()
    {
        var info = Session.PartInfo;
        if (_syncingPartInfo) return;
        _syncingPartInfo = true;
        try
        {
            string cost = info.Cost?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
            if (PartNumber != info.PartNumber) PartNumber = info.PartNumber;
            if (PartDescription != info.Description) PartDescription = info.Description;
            if (PartRevision != info.Revision) PartRevision = info.Revision;
            if (PartMaterial != info.Material) PartMaterial = info.Material;
            if (PartSupplier != info.Supplier) PartSupplier = info.Supplier;
            if (PartCost != cost) PartCost = cost;
            if (PartTags != info.TagText) PartTags = info.TagText;
            if (PartCustom != info.CustomText) PartCustom = info.CustomText;
        }
        finally { _syncingPartInfo = false; }
    }

    /// <summary>Save what is typed in the panel into the document.</summary>
    void PushPartInfo()
    {
        if (_syncingPartInfo) return;
        double? cost = double.TryParse(PartCost.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var c) ? c : double.TryParse(PartCost.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out c) ? c : null;
        var tags = PartInfo.ParseTags(PartTags); var custom = PartInfo.ParseCustom(PartCustom);
        Session.SetPartInfo(new PartInfo(PartNumber.Trim(), PartDescription.Trim(), PartRevision.Trim(), PartMaterial.Trim(), PartSupplier.Trim(), cost, tags.Count == 0 ? null : tags, custom.Count == 0 ? null : custom));
    }
}
