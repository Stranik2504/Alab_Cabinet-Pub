using Newtonsoft.Json;

namespace ALab_Cabinet.Utils;

public class Manager(string filename)
{
    public Action? Loaded;
    public Action? Saved;

    private readonly Mutex _saveLoad = new();

    public T? Load<T>()
    {
        try
        {
            _saveLoad.WaitOne();

            using var stream = File.OpenText(filename);

            var obj = JsonConvert.DeserializeObject<T>(stream.ReadToEnd());

            _saveLoad.ReleaseMutex();

            Loaded?.Invoke();

            return obj;
        }
        catch
        {
            _saveLoad.Close(); 
        }

        return default;
    }

    public void Save<T>(T obj)
    {
        try
        {
            _saveLoad.WaitOne();

            using var stream = File.CreateText(filename);

            stream.WriteLine(JsonConvert.SerializeObject(obj));

            _saveLoad.ReleaseMutex();

            Saved?.Invoke();
        }
        catch
        {
            _saveLoad.Close(); 
        }
    }
}