namespace OctaGrid.Protocol.Users;

public record CreateUserRequest(string Name, string Email, string Password);
