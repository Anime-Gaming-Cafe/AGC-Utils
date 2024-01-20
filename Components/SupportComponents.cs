﻿#region

using AGC_Management.Services;

#endregion

namespace AGC_Management.Components;

public class SupportComponents
{
    public static async Task<Dictionary<string, string>> GetSupportCategories()
    {
        List<string> columns = new()
        {
            "custom_id",
            "category_text"
        };

        List<Dictionary<string, object>> query =
            await DatabaseService.SelectDataFromTable("ticketcategories", columns, null);
        Dictionary<string, string> categories = new();

        foreach (var category in query)
        {
            categories.Add(category["custom_id"].ToString(), category["category_text"].ToString());
        }

        return categories;
    }
}