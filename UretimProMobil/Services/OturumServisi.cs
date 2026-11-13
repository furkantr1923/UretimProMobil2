using UretimProMobil.Models;

namespace UretimProMobil.Services;

public class OturumServisi
{
    public Kullanici? Kullanici { get; private set; }
    public bool GirisYapti => Kullanici != null;
    public event Action? Degisti;

    public void Giris(Kullanici k) { Kullanici = k; Degisti?.Invoke(); }
    public void Cikis() { Kullanici = null; Degisti?.Invoke(); }
    public bool RoldeMi(params string[] roller) => Kullanici != null && roller.Contains(Kullanici.Rol);
}
