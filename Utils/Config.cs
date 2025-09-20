namespace ALab_Cabinet.Utils;

public class Config
{
    public string ConnectionNocoDbUrl { get; set; }
    public string TokenNocoDb { get; set; }
    public string NameDbNocoDb { get; set; }
    public string NameDataNocoDb { get; set; }
    public string TinkoffTerminalKey { get; set; }
    public string TinkoffPassword { get; set; }
    public string BotToken { get; set; }
    public string BotName { get; set; }
    public List<string> Admins { get; set; }
    public bool SendOnlyDev { get; set; } = true;
}