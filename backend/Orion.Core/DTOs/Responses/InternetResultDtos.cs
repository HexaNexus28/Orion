namespace Orion.Core.DTOs.Responses;

public class WebSearchResultDto
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public string? Source { get; set; }
}

public class WebFetchResultDto
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int WordCount { get; set; }
}

public class ScreenshotResultDto
{
    public string Url { get; set; } = string.Empty;
    public string Base64Image { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
}
