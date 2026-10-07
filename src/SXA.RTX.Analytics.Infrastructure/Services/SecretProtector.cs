using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SXA.RTX.Analytics.Infrastructure.Services;

/// <summary>
/// Protege los secretos (cadenas de conexion) con DPAPI de Windows.
/// DPAPI ata el cifrado a la identidad del proceso (App Pool o servicio), que es
/// justo lo que se busca: solo la cuenta que corre la app puede descifrarlos.
/// Si DPAPI no esta disponible se degrada a texto plano Leaving a warning en el log.
/// </summary>
public sealed class SecretProtector
{
    private const string Prefix = "dpapi:";
    private readonly ILogger<SecretProtector> _logger;

    public SecretProtector(ILogger<SecretProtector> logger) => _logger = logger;

    public bool TryProtect(string? plain, out string stored)
    {
        stored = "";
        if (string.IsNullOrEmpty(plain)) return false;
        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.LocalMachine);
            stored = Prefix + Convert.ToBase64String(bytes);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DPAPI no disponible; la cadena se guardara sin cifrar");
            stored = plain;
            return false;
        }
    }

    public string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (!stored.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return stored; // guardado en claro
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(stored[Prefix.Length..]), null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo descifrar un secreto guardado; se devuelve vacio");
            return "";
        }
    }
}