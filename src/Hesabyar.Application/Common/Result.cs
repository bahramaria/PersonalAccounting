namespace Hesabyar.Application.Common;
public sealed record Error(string Code, string Message)
{
    public static Error None { get; } = new(string.Empty, string.Empty);
}
public sealed class Result<T>
{
    private Result(bool isSuccess, T? value, Error error) { IsSuccess = isSuccess; Value = value; Error = error; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public Error Error { get; }
    public static Result<T> Success(T value) => new(true, value, Error.None);
    public static Result<T> Failure(string code, string message) => new(false, default, new Error(code, message));
}