namespace RuSkraping.Models;

public class CookieData
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Path { get; set; } = "/";
    public bool Secure { get; set; }
    public bool HttpOnly { get; set; }
    public DateTime? Expires { get; set; }
    public bool Session { get; set; }
}

