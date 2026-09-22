using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HealthExam.Application.RegistrationForms;

public static class RegistrationFormNormalizer
{
    public static IReadOnlyList<RegistrationFormNode> NormalizeNodes(
        IEnumerable<JsonElement> rawNodes,
        int itemGroupId,
        IReadOnlyDictionary<Guid, (string Value, string Text)> currentValues = null)
    {
        var list = new List<JsonElement>(rawNodes);
        list.Sort((a, b) =>
        {
            var orderA = GetOrderKey(a);
            var orderB = GetOrderKey(b);
            return string.Compare(orderA, orderB, StringComparison.OrdinalIgnoreCase);
        });

        var nodes = new List<RegistrationFormNode>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            var n = list[i];
            var nodeId = GetString(n, "NodeID") ?? GetString(n, "ItemID") ?? GetString(n, "DetailID") ?? GetString(n, "ID") ?? GetString(n, "ItemCode") ?? $"N_{i + 1}";
            var parentId = GetString(n, "ParentNodeID") ?? GetString(n, "ParentItemID");
            if (parentId == "0" || string.IsNullOrWhiteSpace(parentId)) parentId = null;

            var order = GetInt(n, "Order") ?? GetInt(n, "OrderNo") ?? (int.TryParse(GetString(n, "OrderString"), out var parsedOrder) ? parsedOrder : (i + 1));
            var level = GetInt(n, "Level") ?? GetInt(n, "LevelNo") ?? 0;
            var label = GetString(n, "Label") ?? GetString(n, "ItemDesc") ?? GetString(n, "ItemName") ?? GetString(n, "DisplayName") ?? "";
            var controlType = GetString(n, "ControlType") ?? "TXT";
            var dataType = GetString(n, "DataType") ?? "S";
            var required = GetBool(n, "Required") ?? GetBool(n, "IsObligatory") ?? GetBool(n, "IsRequired") ?? false;
            var readOnly = GetBool(n, "ReadOnly") ?? GetBool(n, "IsReadOnly") ?? false;

            var isLabelOrGroup = string.Equals(controlType, "LBL", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(controlType, "GRP", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(controlType, "LABEL", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(controlType, "GROUP", StringComparison.OrdinalIgnoreCase);

            Guid? itemId = null;
            if (!isLabelOrGroup)
            {
                var itemIdStr = GetString(n, "ItemID") ?? GetString(n, "ItemId") ?? GetString(n, "NodeID");
                if (!string.IsNullOrEmpty(itemIdStr) && Guid.TryParse(itemIdStr, out var parsedGuid) && parsedGuid != Guid.Empty)
                {
                    itemId = parsedGuid;
                }
            }

            string value;
            string text;
            if (itemId.HasValue && currentValues != null && currentValues.TryGetValue(itemId.Value, out var cv))
            {
                value = cv.Value;
                text = cv.Text;
            }
            else
            {
                value = GetString(n, "Value") ?? "";
                text = GetString(n, "Text");
            }

            List<RegistrationFormChoice> choices = null;
            if (n.TryGetProperty("Choices", out var choicesProp) && choicesProp.ValueKind == JsonValueKind.Array)
            {
                choices = ParseChoices(choicesProp);
            }
            else if (n.TryGetProperty("Options", out var optsProp) && optsProp.ValueKind == JsonValueKind.Array)
            {
                choices = ParseChoices(optsProp);
            }

            nodes.Add(new RegistrationFormNode
            {
                NodeID = nodeId,
                ParentNodeID = parentId,
                ItemID = itemId,
                ItemGroupID = itemGroupId,
                Order = order,
                Level = level,
                Label = label,
                ControlType = controlType,
                DataType = dataType,
                Required = required,
                ReadOnly = readOnly,
                Value = value,
                Text = text,
                Choices = choices
            });
        }
        return nodes;
    }

    public static string GetOrderKey(JsonElement elem)
    {
        return GetString(elem, "OrderString") ?? GetString(elem, "OrderNo") ?? "";
    }

    public static string GetString(JsonElement elem, string propName)
    {
        if (elem.TryGetProperty(propName, out var p))
        {
            if (p.ValueKind == JsonValueKind.String) return p.GetString();
            if (p.ValueKind == JsonValueKind.Number) return p.ToString();
            if (p.ValueKind == JsonValueKind.True) return "true";
            if (p.ValueKind == JsonValueKind.False) return "false";
        }
        if (elem.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in elem.EnumerateObject())
            {
                if (string.Equals(item.Name, propName, StringComparison.OrdinalIgnoreCase))
                {
                    if (item.Value.ValueKind == JsonValueKind.String) return item.Value.GetString();
                    if (item.Value.ValueKind == JsonValueKind.Number) return item.Value.ToString();
                    if (item.Value.ValueKind == JsonValueKind.True) return "true";
                    if (item.Value.ValueKind == JsonValueKind.False) return "false";
                }
            }
        }
        return null;
    }

    public static int? GetInt(JsonElement elem, string propName)
    {
        if (elem.TryGetProperty(propName, out var p))
        {
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var val)) return val;
            if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var sVal)) return sVal;
        }
        if (elem.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in elem.EnumerateObject())
            {
                if (string.Equals(item.Name, propName, StringComparison.OrdinalIgnoreCase))
                {
                    if (item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetInt32(out var val)) return val;
                    if (item.Value.ValueKind == JsonValueKind.String && int.TryParse(item.Value.GetString(), out var sVal)) return sVal;
                }
            }
        }
        return null;
    }

    public static bool? GetBool(JsonElement elem, string propName)
    {
        if (elem.TryGetProperty(propName, out var p))
        {
            if (p.ValueKind == JsonValueKind.True) return true;
            if (p.ValueKind == JsonValueKind.False) return false;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var nVal)) return nVal != 0;
            if (p.ValueKind == JsonValueKind.String && bool.TryParse(p.GetString(), out var bVal)) return bVal;
        }
        if (elem.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in elem.EnumerateObject())
            {
                if (string.Equals(item.Name, propName, StringComparison.OrdinalIgnoreCase))
                {
                    if (item.Value.ValueKind == JsonValueKind.True) return true;
                    if (item.Value.ValueKind == JsonValueKind.False) return false;
                    if (item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetInt32(out var nVal)) return nVal != 0;
                    if (item.Value.ValueKind == JsonValueKind.String && bool.TryParse(item.Value.GetString(), out var bVal)) return bVal;
                }
            }
        }
        return null;
    }

    public static List<RegistrationFormChoice> ParseChoices(JsonElement arrayElem)
    {
        var list = new List<RegistrationFormChoice>();
        foreach (var c in arrayElem.EnumerateArray())
        {
            var code = GetString(c, "Code") ?? GetString(c, "Value") ?? "";
            var label = GetString(c, "Label") ?? GetString(c, "Name") ?? "";
            list.Add(new RegistrationFormChoice { Code = code, Label = label });
        }
        return list;
    }
}
