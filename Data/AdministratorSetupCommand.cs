using System.Text;
using MiniWorkflowEngine.Services;

namespace MiniWorkflowEngine.Data;

public static class AdministratorSetupCommand
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Yönetici kurulumu etkileşimli terminal gerektirir. Parolayı komut satırı veya dosya üzerinden vermeyin.");
            return 1;
        }
        Console.Write("Yönetici e-posta adresi: ");
        var email = Console.ReadLine() ?? string.Empty;
        Console.Write("Ad soyad: ");
        var fullName = Console.ReadLine() ?? string.Empty;
        Console.WriteLine("Parola en az 10 karakter; büyük harf, küçük harf, rakam ve özel karakter içermelidir.");
        Console.Write("Parola: ");
        var password = ReadPassword();
        Console.Write("Parola tekrarı: ");
        if (password != ReadPassword())
        {
            Console.Error.WriteLine("Parolalar eşleşmiyor.");
            return 1;
        }
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedRolesAsync();
        var result = await scope.ServiceProvider.GetRequiredService<AdministratorBootstrapper>()
            .CreateAsync(email, fullName, password);
        if (result.Succeeded)
        {
            Console.WriteLine("Yönetici hesabı oluşturuldu. HTTPS adresinden giriş yapabilirsiniz.");
            return 0;
        }
        foreach (var error in result.Errors) Console.Error.WriteLine(error.Description);
        return 1;
    }

    private static string ReadPassword()
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return buffer.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (buffer.Length > 0) buffer.Length--; }
            else if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
        }
    }
}
