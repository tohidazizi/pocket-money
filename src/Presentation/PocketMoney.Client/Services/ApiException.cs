using PocketMoney.Client.Models;

namespace PocketMoney.Client.Services;

/// <summary>Thrown when the API returns a non-2xx ProblemDetails body.</summary>
public sealed class ApiException : Exception
{
    public ApiProblem Problem { get; }
    public int Status => Problem.Status;
    public string Code => Problem.Code;

    public ApiException(ApiProblem problem)
        : base(problem.Display) => Problem = problem;
}
