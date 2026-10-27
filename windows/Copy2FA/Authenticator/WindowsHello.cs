using Windows.Security.Credentials.UI;

namespace Copy2FA.Authenticator;

internal enum HelloResult { Verified, Canceled, Failed, Unavailable }

/// <summary>Asks Windows Hello (face, fingerprint or PIN) to confirm it's you before accounts are shown.</summary>
internal static class WindowsHello
{
    public static async Task<HelloResult> VerifyAsync(IntPtr owner, string reason)
    {
        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            if (availability != UserConsentVerifierAvailability.Available) return HelloResult.Unavailable;

            // The window-based call is the one desktop apps use; it shows the prompt over the given window.
            var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(owner, reason);
            return result switch
            {
                UserConsentVerificationResult.Verified => HelloResult.Verified,
                UserConsentVerificationResult.Canceled => HelloResult.Canceled,
                UserConsentVerificationResult.DeviceNotPresent or UserConsentVerificationResult.NotConfiguredForUser
                    or UserConsentVerificationResult.DisabledByPolicy => HelloResult.Unavailable,
                _ => HelloResult.Failed,
            };
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return HelloResult.Unavailable;
        }
    }
}
