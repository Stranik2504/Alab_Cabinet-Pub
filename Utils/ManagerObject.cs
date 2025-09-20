namespace ALab_Cabinet.Utils;

public class ManagerObject<T>(string filename)
{
    public T? Obj { get; set; }

    public Action? Loaded { get => _manager.Loaded; set => _manager.Loaded = value; }
    public Action? Saved { get => _manager.Saved; set => _manager.Saved = value; }
    
    private readonly Manager _manager = new(filename);

    public void Load()
    {
        if (!File.Exists(filename))
            _manager.Save(Obj);
            
        Obj = _manager.Load<T>();
    }

    public void Save() => _manager.Save(Obj);   
}