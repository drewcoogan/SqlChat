using Microsoft.EntityFrameworkCore;
using SqlChat.Data.Providers;

namespace SqlChat.Tests.Data;

public sealed class EfCoreSchemaProviderTests
{
    private sealed class Blog
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public List<Post> Posts { get; set; } = new();
    }

    private sealed class Post
    {
        public int Id { get; set; }
        public int BlogId { get; set; }
        public Blog Blog { get; set; } = null!;
        public string Title { get; set; } = "";
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<Blog> Blogs => Set<Blog>();
        public DbSet<Post> Posts => Set<Post>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Blog>(b =>
            {
                b.ToTable("Blogs");
                b.HasKey(x => x.Id);
                b.Property(x => x.Name).HasColumnType("nvarchar(200)").IsRequired();
            });

            modelBuilder.Entity<Post>(p =>
            {
                p.ToTable("Posts");
                p.HasKey(x => x.Id);
                p.Property(x => x.Title).HasColumnType("nvarchar(500)").IsRequired();
                p.HasOne(x => x.Blog).WithMany(x => x.Posts).HasForeignKey(x => x.BlogId);
            });
        }
    }

    [Fact]
    public async Task GetSchemaAsync_includes_tables_columns_pk_and_fk()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new TestDbContext(options);
        var provider = new EfCoreSchemaProvider(db.Model);

        var catalog = await provider.GetSchemaAsync();

        var blogs = Assert.Single(catalog.Tables, t => t.Name == "Blogs");
        var posts = Assert.Single(catalog.Tables, t => t.Name == "Posts");

        var blogId = Assert.Single(blogs.Columns, c => c.Name == "Id");
        Assert.True(blogId.IsPrimaryKey);

        var postBlogId = Assert.Single(posts.Columns, c => c.Name == "BlogId");
        Assert.NotNull(postBlogId.ForeignKey);
        Assert.Equal("Blogs", postBlogId.ForeignKey!.PrincipalTable);
        Assert.Equal("Id", postBlogId.ForeignKey!.PrincipalColumn);
    }
}

