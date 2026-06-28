namespace SouthBaySoccer.Infrastructure.Authentication;

public interface IWhatsAppChallengeTokenGenerator
{
    string CreateToken();
}