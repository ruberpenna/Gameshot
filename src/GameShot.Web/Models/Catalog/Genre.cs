namespace GameShot.Web.Models.Catalog;

public class Genre
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public int SortOrder { get; set; }

    public ICollection<Product> Products { get; set; } = [];
}
