using System.ComponentModel.DataAnnotations;

namespace AIVES.WebMVC.Models.ViewModels;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Please enter your email.")]
    [EmailAddress(ErrorMessage = "The email address is not valid.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "Please enter your full name.")]
    [StringLength(120, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your Gmail address.")]
    [EmailAddress(ErrorMessage = "The email address is not valid.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a password.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "The password must be at least 8 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The password confirmation does not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class VerifyEmailViewModel
{
    [Required]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter the verification code.")]
    [RegularExpression("^[0-9]{6}$", ErrorMessage = "The verification code must be exactly 6 digits.")]
    public string Code { get; set; } = string.Empty;
}
