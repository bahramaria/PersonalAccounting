using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;
using Microsoft.Maui.ApplicationModel;

namespace Hesabyar.App.Services;

public sealed class AndroidBiometricAuthenticator : IBiometricAuthenticator
{
    public Task<bool> IsAvailableAsync() =>
        Task.FromResult(OperatingSystem.IsAndroidVersionAtLeast(28) && Platform.CurrentActivity is not null);

    public Task<bool> AuthenticateAsync(string title, string reason, string cancelText)
    {
        var activity = Platform.CurrentActivity;
        if (!OperatingSystem.IsAndroidVersionAtLeast(28) || activity is null)
            return Task.FromResult(false);

        var executor = activity.MainExecutor;
        if (executor is null) return Task.FromResult(false);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var callback = new PromptCallback(completion);
            var prompt = new BiometricPrompt.Builder(activity)
                .SetTitle(title)
                .SetSubtitle(reason)
                .SetNegativeButton(cancelText, executor, new CancelListener(completion))
                .Build();
            prompt.Authenticate(new CancellationSignal(), executor, callback);
        }
        catch (Exception)
        {
            completion.TrySetResult(false);
        }

        return completion.Task;
    }

    private sealed class PromptCallback(TaskCompletionSource<bool> completion) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result) =>
            completion.TrySetResult(true);

        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString) =>
            completion.TrySetResult(false);
    }

    private sealed class CancelListener(TaskCompletionSource<bool> completion)
        : Java.Lang.Object, IDialogInterfaceOnClickListener
    {
        public void OnClick(IDialogInterface? dialog, int which) => completion.TrySetResult(false);
    }
}
