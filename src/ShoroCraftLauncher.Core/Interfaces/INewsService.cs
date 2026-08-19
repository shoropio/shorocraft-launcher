using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface INewsService
{
    Task<List<NewsItem>> GetNewsAsync();
}
