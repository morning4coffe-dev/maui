using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Xunit;

namespace Tests
{
	public class SecureStorage_Tests
	{
		[Fact]
		public async Task SecureStorage_LoadAsync_Fail_On_NetStandard()
		{
			await Assert.ThrowsAsync<NotImplementedInReferenceAssemblyException>(() => SecureStorage.GetAsync("key"));
		}

		[Fact]
		public async Task SecureStorage_SaveAsync_Fail_On_NetStandard()
		{
			await Assert.ThrowsAsync<NotImplementedInReferenceAssemblyException>(() => SecureStorage.SetAsync("key", "data"));
		}

		[Fact]
		public void SecureStorage_KeyHash_Is_Case_Sensitive()
		{
			var lower = SecureStorageImplementation.HashSecureStorageKey("secure-key");
			var upper = SecureStorageImplementation.HashSecureStorageKey("Secure-Key");

			Assert.NotEqual(lower, upper);
			Assert.Equal(lower, SecureStorageImplementation.HashSecureStorageKey("secure-key"));
		}

		[Fact]
		public void SecureStorage_PasswordVault_Resource_Is_App_Scoped()
		{
			Assert.Equal(
				"com.contoso.sample.microsoft.maui.essentials.securestorage",
				SecureStorageImplementation.GetSecureStoragePasswordVaultResource("com.contoso.sample"));
		}
	}
}
