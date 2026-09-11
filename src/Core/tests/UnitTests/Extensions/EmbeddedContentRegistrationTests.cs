using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Embedding;
using NSubstitute;
using Xunit;

namespace Microsoft.Maui.UnitTests.Extensions
{
	public class EmbeddedContentRegistrationTests
	{
		[Fact]
		public void DuplicateAcquisitionDoesNotReleaseTheOwner()
		{
			var view = new ContentView();
			using (EmbeddedContentRegistration.Acquire(view))
			{
				Assert.Throws<InvalidOperationException>(() => EmbeddedContentRegistration.VerifyAvailable(view));
				Assert.Throws<InvalidOperationException>(() => EmbeddedContentRegistration.Acquire(view));
				Assert.Throws<InvalidOperationException>(() => EmbeddedContentRegistration.Acquire(view));
			}
			EmbeddedContentRegistration.VerifyAvailable(view);
			using var retry = EmbeddedContentRegistration.Acquire(view);
		}

		[Fact]
		public void FailedAttachmentCanBeRetried()
		{
			var view = new ContentView();
			Action attach = () =>
			{
				using var registration = EmbeddedContentRegistration.Acquire(view);
				throw new NotSupportedException("Attachment failed.");
			};
			Assert.Throws<NotSupportedException>(attach);
			using var retry = EmbeddedContentRegistration.Acquire(view);
		}

		[Fact]
		public void DisposingAnOldRegistrationDoesNotReleaseANewerOwner()
		{
			var view = new ContentView();
			var previous = EmbeddedContentRegistration.Acquire(view);
			previous.Dispose();
			using var current = EmbeddedContentRegistration.Acquire(view);
			previous.Dispose();

			Assert.Throws<InvalidOperationException>(() => EmbeddedContentRegistration.Acquire(view));
		}

		[Fact]
		public void RegistrationOnlyAuthorizesItsOwnContentAndLifetime()
		{
			var view = new ContentView();
			var registration = EmbeddedContentRegistration.Acquire(view);
			registration.VerifyOwnership(view);
			Assert.Throws<InvalidOperationException>(() => registration.VerifyOwnership(new ContentView()));
			registration.Dispose();
			Assert.Throws<InvalidOperationException>(() => registration.VerifyOwnership(view));
		}

		[Fact]
		public void ParentedContentIsRejectedWithoutMutation()
		{
			var view = new ContentView();
			var parent = new ContentView { Content = view };

			Assert.Throws<InvalidOperationException>(() => EmbeddedContentRegistration.Acquire(view));
			Assert.Same(parent, view.Parent);
			Assert.Same(view, parent.Content);
			parent.Content = null;
			using var retry = EmbeddedContentRegistration.Acquire(view);
		}

		[Fact]
		public void ExistingHandlerIsRejectedWithoutMutation()
		{
			var content = new ContentView();
			var handler = Substitute.For<IViewHandler>();
			((IElement)content).Handler = handler;

			Assert.Throws<InvalidOperationException>(() => EmbeddedContentRegistration.Acquire(content));
			Assert.Same(handler, content.Handler);
			handler.DidNotReceive().DisconnectHandler();
		}
	}
}
