#region

using AGC_Management.Services;

#endregion

namespace AGC_Management.Components;

public class SupportComponents
{
    public static async Task<Dictionary<string, string>> GetSupportCategories()
    {
        List<string> columns =
		[
			"custom_id",
            "category_text"
        ];

        var query =
            await DatabaseService.SelectDataFromTable("ticketcategories", columns, null);
        Dictionary<string, string> categories = [];

        foreach (var category in query)
            categories.Add(category["custom_id"].ToString(), category["category_text"].ToString());

        return categories;
    }
}