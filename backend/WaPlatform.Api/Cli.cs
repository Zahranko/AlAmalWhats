using Microsoft.EntityFrameworkCore;
using WaPlatform.Api.Auth;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;

namespace WaPlatform.Api;

/// <summary>
/// Install-time commands, run instead of the web server:
///   dotnet run -- migrate
///   dotnet run -- create-admin --email admin@example.com --name "Admin" [--password ...]
/// </summary>
public static class Cli
{
    public static bool IsCommand(string[] args) => args.Length > 0 && args[0] is "migrate" or "create-admin";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Console.WriteLine("Applying database migrations...");
        await db.Database.MigrateAsync();

        return args[0] switch
        {
            "migrate" => 0,
            "create-admin" => await CreateAdmin(db, ParseOptions(args[1..])),
            _ => 1,
        };
    }

    private static async Task<int> CreateAdmin(AppDbContext db, Dictionary<string, string> opts)
    {
        if (!opts.TryGetValue("email", out var rawEmail) || !opts.TryGetValue("name", out var name))
        {
            Console.Error.WriteLine("Usage: create-admin --email <email> --name <name> [--password <password>]");
            return 1;
        }

        var email = AuthSetup.NormalizeEmail(rawEmail);
        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            Console.Error.WriteLine($"A user with email {email} already exists.");
            return 1;
        }

        var password = opts.GetValueOrDefault("password") ?? ReadHidden("Password: ");
        if (Passwords.Validate(password) is { } problem)
        {
            Console.Error.WriteLine(problem);
            return 1;
        }

        var user = new User { Name = name, Email = email, Role = Roles.Admin, PasswordHash = Passwords.Hash(password) };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.AuditLog.Add(new AuditLogEntry
        {
            Action = AuditActions.AdminBootstrapped,
            Target = $"user:{user.Id} {user.Email}",
            Details = "{\"source\":\"cli\"}",
        });
        await db.SaveChangesAsync();

        Console.WriteLine($"Admin {email} created.");
        return 0;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].StartsWith("--"))
                result[args[i][2..]] = args[++i];
        return result;
    }

    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); }
            else chars.Add(key.KeyChar);
        }
        Console.WriteLine();
        return new string(chars.ToArray());
    }
}
