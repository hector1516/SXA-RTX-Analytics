using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SXA.RTX.Analytics.Domain.Entities;
using SXA.RTX.Analytics.Infrastructure.Persistence;
using SXA.RTX.Analytics.Infrastructure.Services;

namespace SXA.RTX.Analytics.Infrastructure.Tests;

public class BootstrapAdminServiceTests : InfrastructureTestBase
{
    [Fact]
    public async Task Sin_administrador_crea_el_superadmin_por_defecto()
    {
        await using var db = NewDb(nameof(Sin_administrador_crea_el_superadmin_por_defecto));
        var sut = new BootstrapAdminService(db, NullLogger<BootstrapAdminService>.Instance);

        var r = await sut.EnsureAsync("ECCSA", "", "ECCSA", resetPassword: false, defaultPassword: "Qwe123456");

        Assert.Equal("created", r);
        var user = await db.Set<AppUser>().SingleAsync();
        Assert.Equal(AppRole.Administrador, user.Role);
        Assert.False(user.MustChangePassword);
        Assert.True(BCrypt.Net.BCrypt.Verify("Qwe123456", user.PasswordHash));
    }

    [Fact]
    public async Task Crea_el_superadmin_cuando_no_hay_administrador_y_hay_password()
    {
        await using var db = NewDb(nameof(Crea_el_superadmin_cuando_no_hay_administrador_y_hay_password));
        var sut = new BootstrapAdminService(db, NullLogger<BootstrapAdminService>.Instance);

        var r = await sut.EnsureAsync("ECCSA", "Temp#123456", "ECCSA", resetPassword: false, defaultPassword: "Qwe123456");

        Assert.Equal("created", r);
        var user = await db.Set<AppUser>().SingleAsync();
        Assert.Equal(AppRole.Administrador, user.Role);
        Assert.True(BCrypt.Net.BCrypt.Verify("Temp#123456", user.PasswordHash));
    }

    [Fact]
    public async Task No_toca_nada_si_ya_hay_administrador()
    {
        await using var db = NewDb(nameof(No_toca_nada_si_ya_hay_administrador));
        db.Set<AppUser>().Add(new AppUser
        {
            Username = "otro", PasswordHash = BCrypt.Net.BCrypt.HashPassword("X#12345678"),
            DisplayName = "Otro", Role = AppRole.Administrador,
        });
        await db.SaveChangesAsync();
        var sut = new BootstrapAdminService(db, NullLogger<BootstrapAdminService>.Instance);

        var r = await sut.EnsureAsync("ECCSA", "Temp#123456", "ECCSA", resetPassword: false, defaultPassword: "Qwe123456");

        Assert.Equal("ok", r);
        Assert.Single(await db.Set<AppUser>().ToListAsync());
    }

    [Fact]
    public async Task Reset_resetea_la_password_del_superadmin_existente()
    {
        await using var db = NewDb(nameof(Reset_resetea_la_password_del_superadmin_existente));
        db.Set<AppUser>().Add(new AppUser
        {
            Username = "ECCSA", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Olvidada#1"),
            DisplayName = "ECCSA", Role = AppRole.Administrador, MustChangePassword = false,
        });
        await db.SaveChangesAsync();
        var sut = new BootstrapAdminService(db, NullLogger<BootstrapAdminService>.Instance);

        var r = await sut.EnsureAsync("ECCSA", "Nueva#123456", "ECCSA", resetPassword: true, defaultPassword: "Qwe123456");

        Assert.Equal("reset", r);
        var user = await db.Set<AppUser>().SingleAsync();
        Assert.True(BCrypt.Net.BCrypt.Verify("Nueva#123456", user.PasswordHash));
        Assert.True(user.MustChangePassword);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task Reset_con_username_inexistente_lo_crea()
    {
        await using var db = NewDb(nameof(Reset_con_username_inexistente_lo_crea));
        var sut = new BootstrapAdminService(db, NullLogger<BootstrapAdminService>.Instance);

        var r = await sut.EnsureAsync("superadmin", "Temp#123456", "Super Admin", resetPassword: true, defaultPassword: "Qwe123456");

        Assert.Equal("reset", r);
        var user = await db.Set<AppUser>().SingleAsync();
        Assert.Equal("superadmin", user.Username);
        Assert.Equal(AppRole.Administrador, user.Role);
    }
}

public class AuthServiceChangePasswordTests : InfrastructureTestBase
{
    private static AuthService NewService(ConfigurationDbContext db) =>
        new(db, NullLogger<AuthService>.Instance);

    [Fact]
    public async Task Login_devuelve_must_change_cuando_el_usuario_lo_tiene()
    {
        await using var db = NewDb(nameof(Login_devuelve_must_change_cuando_el_usuario_lo_tiene));
        db.Set<AppUser>().Add(new AppUser
        {
            Username = "ECCSA", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Temp#123456"),
            DisplayName = "ECCSA", Role = AppRole.Administrador, MustChangePassword = true,
        });
        await db.SaveChangesAsync();
        var sut = NewService(db);

        var res = await sut.LoginAsync("ECCSA", "Temp#123456");

        Assert.True(res.Success);
        Assert.True(res.MustChangePassword);
    }

    [Fact]
    public async Task ChangePassword_actualiza_y_limpia_el_flag()
    {
        await using var db = NewDb(nameof(ChangePassword_actualiza_y_limpia_el_flag));
        db.Set<AppUser>().Add(new AppUser
        {
            Username = "ECCSA", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Temp#123456"),
            DisplayName = "ECCSA", Role = AppRole.Administrador, MustChangePassword = true,
        });
        await db.SaveChangesAsync();
        var sut = NewService(db);

        var res = await sut.ChangePasswordAsync("ECCSA", "Temp#123456", "Nueva#123456");

        Assert.True(res.Success, res.Message);
        var user = await db.Set<AppUser>().SingleAsync();
        Assert.False(user.MustChangePassword);
        Assert.True(BCrypt.Net.BCrypt.Verify("Nueva#123456", user.PasswordHash));
        // La contraseña vieja ya no vale
        var login = await sut.LoginAsync("ECCSA", "Temp#123456");
        Assert.False(login.Success);
    }

    [Fact]
    public async Task ChangePassword_rechaza_password_corta()
    {
        await using var db = NewDb(nameof(ChangePassword_rechaza_password_corta));
        db.Set<AppUser>().Add(new AppUser
        {
            Username = "ECCSA", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Temp#123456"),
            DisplayName = "ECCSA", Role = AppRole.Administrador,
        });
        await db.SaveChangesAsync();
        var sut = NewService(db);

        var res = await sut.ChangePasswordAsync("ECCSA", "Temp#123456", "corta");

        Assert.False(res.Success);
    }

    [Fact]
    public async Task ChangePassword_rechaza_contraseña_actual_incorrecta()
    {
        await using var db = NewDb(nameof(ChangePassword_rechaza_contraseña_actual_incorrecta));
        db.Set<AppUser>().Add(new AppUser
        {
            Username = "ECCSA", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Temp#123456"),
            DisplayName = "ECCSA", Role = AppRole.Administrador,
        });
        await db.SaveChangesAsync();
        var sut = NewService(db);

        var res = await sut.ChangePasswordAsync("ECCSA", "equivocada", "Nueva#123456");

        Assert.False(res.Success);
        Assert.Contains("actual", res.Message, StringComparison.OrdinalIgnoreCase);
    }
}