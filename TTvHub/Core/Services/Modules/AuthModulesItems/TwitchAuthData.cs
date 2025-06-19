using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TTvHub.Core.Services.Modules.AuthModulesItems;

public class TwitchAuthData
{
    [Key]
    public int Id { get; set; } 

    public string Login { get; set; } = string.Empty;
    public string TwitchUserId { get; set; } = string.Empty; 

    public string EncryptedAccessToken { get; set; } = string.Empty;
    public string EncryptedRefreshToken { get; set; } = string.Empty;

    [NotMapped]
    public string AccessToken { get; set; } = string.Empty;
    [NotMapped]
    public string RefreshToken { get; set; } = string.Empty;
}