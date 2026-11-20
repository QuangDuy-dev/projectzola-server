namespace MySocialApp.Application.Interfaces;

public interface IAppKnowledgeService
{
    Task<string> GetRelevantKnowledgeAsync(string userMessage);
    string RouteCategory(string userMessage);
}
