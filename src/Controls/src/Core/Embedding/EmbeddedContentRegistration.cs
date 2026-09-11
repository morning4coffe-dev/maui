using System;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.Controls.Embedding
{
	internal sealed class EmbeddedContentRegistration : IDisposable
	{
		static readonly object Gate = new();
		static readonly ConditionalWeakTable<VisualElement, EmbeddedContentRegistration> Owners = new();
		readonly VisualElement _content;

		EmbeddedContentRegistration(VisualElement content) => _content = content;

		internal static EmbeddedContentRegistration Acquire(VisualElement content)
		{
			_ = content ?? throw new ArgumentNullException(nameof(content));
			lock (Gate)
			{
				VerifyAvailableCore(content);
				var registration = new EmbeddedContentRegistration(content);
				Owners.Add(content, registration);
				return registration;
			}
		}

		internal static void VerifyAvailable(VisualElement content)
		{
			lock (Gate)
				VerifyAvailableCore(content);
		}

		static void VerifyAvailableCore(VisualElement content)
		{
			if (Owners.TryGetValue(content, out _))
				throw new InvalidOperationException("This MAUI element already belongs to an embedding operation or host.");

			VerifyDetached(content);
		}

		internal void VerifyOwnership(VisualElement content)
		{
			lock (Gate)
			{
				if (!ReferenceEquals(content, _content) ||
					!Owners.TryGetValue(content, out var owner) || !ReferenceEquals(owner, this))
					throw new InvalidOperationException("The embedding registration no longer owns this MAUI element.");

				VerifyDetached(content);
			}
		}

		static void VerifyDetached(VisualElement content)
		{
			if (content.Parent is not null || content.Handler is not null)
				throw new InvalidOperationException("Only an unparented MAUI element without a handler can be embedded. Release its existing owner first.");
		}

		public void Dispose()
		{
			lock (Gate)
			{
				if (Owners.TryGetValue(_content, out var owner) && ReferenceEquals(owner, this))
					Owners.Remove(_content);
			}
		}
	}
}
