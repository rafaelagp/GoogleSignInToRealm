using Android.App;
using Android.OS;
using Android.Views;
using System;
using Android.Gms.Common.Apis;
using Android.Gms.Common;
using Android.Gms.Auth.Api.SignIn;
using Android.Gms.Auth.Api;
using Android.Widget;
using Android.Util;
using Android.Content;
using Realms.Sync;
using System.Threading.Tasks;
using Realms;
using Android.Graphics;

namespace GoogleSignInToRealmObjectServerPOC
{
	[Activity(Label = "@string/app_name", MainLauncher = true, Icon = "@mipmap/icon")]
	public class MainActivity : Activity, View.IOnClickListener,
		GoogleApiClient.IConnectionCallbacks, GoogleApiClient.IOnConnectionFailedListener
	{
		const int RC_SIGN_IN = 9001;
		const string KEY_IS_RESOLVING = "is_resolving";
		const string KEY_SHOULD_RESOLVE = "should_resolve";
		const string TAG = "MainActivity";

		bool IsResolving;
		bool ShouldResolve;

		GoogleApiClient GoogleApiClient;

		protected override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);
			SetContentView(Resource.Layout.Main);

			if (savedInstanceState != null)
			{
				IsResolving = savedInstanceState.GetBoolean(KEY_IS_RESOLVING);
				ShouldResolve = savedInstanceState.GetBoolean(KEY_SHOULD_RESOLVE);
			}

			FindViewById(Resource.Id.sign_in_button).SetOnClickListener(this);
			FindViewById(Resource.Id.sign_out_button).SetOnClickListener(this);

			//TODO remove oauth client id
			var gso = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
			    .RequestIdToken("oauth_client_id")
			    .RequestEmail()
				.RequestProfile()
				.Build();
			GoogleApiClient = new GoogleApiClient.Builder(this)
				.AddConnectionCallbacks(this)
				.AddOnConnectionFailedListener(this)
				.AddApi(Auth.GOOGLE_SIGN_IN_API, gso)
			    .AddScope(new Scope(Scopes.Email))
				.AddScope(new Scope(Scopes.Profile))
				.Build();
		}

		async Task DownloadProfileAvatar(string url)
		{
			try
			{
				var filename = System.IO.Path.GetFileName(url);
				var documentsPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
				var filePath = System.IO.Path.Combine(documentsPath, filename);

				if (!System.IO.File.Exists(filePath))
				{
					var response = await new System.Net.Http.HttpClient().GetAsync(url);
					var contentStream = await response?.Content?.ReadAsStreamAsync();
					
					using (var fileStream = new System.IO.FileStream(filePath,
					                                                 System.IO.FileMode.Create,
					                                                 System.IO.FileAccess.Write))
					{
						contentStream.CopyTo(fileStream);
					}
				}

				var bitmap = await BitmapFactory.DecodeStreamAsync(new System.IO.FileStream(filePath, 
							                                                                System.IO.FileMode.Open, 
							                                                                System.IO.FileAccess.Read));
				RunOnUiThread(() =>
				{
					var imageView = FindViewById<ImageView>(Resource.Id.profile_avatar);
					imageView.SetImageBitmap(bitmap);
				});
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine(ex);
			}
		}

		async Task SyncRealm(GoogleSignInAccount googleAccount)
		{
			try
			{
				var user = await User.LoginAsync(Credentials.Google(googleAccount.IdToken), 
				                                 new Uri("http://realm_object_server_url:port/"));
				var config = new SyncConfiguration(user,
				                                   new Uri("realm://realm_object_server_url:port/~/testrealm"))
				{
					SchemaVersion = 1
				};
				
				var realm = await Realm.GetInstanceAsync(config);
				var transaction = realm.BeginWrite();
				realm.Add(new TestTable { Name = googleAccount.DisplayName });
				transaction.Commit();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine(ex);
			}
		}

		void UpdateUI(bool isSignedIn)
		{
			if (isSignedIn)
			{
				FindViewById(Resource.Id.profile_avatar).Visibility = ViewStates.Visible;
				FindViewById(Resource.Id.profile_name).Visibility = ViewStates.Visible;
				FindViewById(Resource.Id.profile_email).Visibility = ViewStates.Visible;
				FindViewById(Resource.Id.sign_out_button).Visibility = ViewStates.Visible;
				FindViewById(Resource.Id.sign_in_button).Visibility = ViewStates.Gone;
			}
			else
			{
				FindViewById(Resource.Id.sign_in_button).Visibility = ViewStates.Visible;
				FindViewById(Resource.Id.profile_avatar).Visibility = ViewStates.Gone;
				FindViewById(Resource.Id.profile_name).Visibility = ViewStates.Gone;
				FindViewById(Resource.Id.profile_email).Visibility = ViewStates.Gone;
				FindViewById(Resource.Id.sign_out_button).Visibility = ViewStates.Gone;
			}
		}

		protected override void OnStart()
		{
			base.OnStart();
			GoogleApiClient.Connect();
		}

		protected override void OnStop()
		{
			GoogleApiClient.Disconnect();
			base.OnStop();
		}

		protected override void OnSaveInstanceState(Bundle outState)
		{
			base.OnSaveInstanceState(outState);
			outState.PutBoolean(KEY_IS_RESOLVING, IsResolving);
			outState.PutBoolean(KEY_SHOULD_RESOLVE, ShouldResolve);
		}

		protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
		{
			base.OnActivityResult(requestCode, resultCode, data);
			Log.Debug(TAG, "onActivityResult:" + requestCode + ":" + resultCode + ":" + data);

			if (requestCode == RC_SIGN_IN)
			{
				if (resultCode != Result.Ok)
				{
					ShouldResolve = false;
				}
				else
				{
					var googleAccount = Auth.GoogleSignInApi.GetSignInResultFromIntent(data).SignInAccount;
					Task.Run(async () => await DownloadProfileAvatar(googleAccount.PhotoUrl.ToString()));
					Task.Run(async () => await SyncRealm(googleAccount));

					FindViewById<TextView>(Resource.Id.profile_name).Text = googleAccount.DisplayName;
					FindViewById<TextView>(Resource.Id.profile_email).Text = googleAccount.Email;

					UpdateUI(true);
				}

				IsResolving = false;
				GoogleApiClient.Connect();
			}
		}

		public void OnConnected(Bundle connectionHint)
		{
			Log.Debug(TAG, "onConnected:" + connectionHint);
		}
		
		public void OnConnectionFailed(ConnectionResult result)
		{
			Log.Debug(TAG, "onConnectionFailed:" + result);

			if (!IsResolving && ShouldResolve)
			{
				if (result.HasResolution)
				{
					try
					{
						result.StartResolutionForResult(this, RC_SIGN_IN);
						IsResolving = true;
					}
					catch (IntentSender.SendIntentException e)
					{
						System.Diagnostics.Debug.WriteLine("Could not resolve ConnectionResult.", e);
						Log.Error(TAG, "Could not resolve ConnectionResult.", e);
						IsResolving = false;
						GoogleApiClient.Connect();
					}
				}
				else
				{
					ShowErrorDialog(result);
				}
			}
			else
			{
				UpdateUI(false);
			}
		}
		
		public void OnConnectionSuspended(int cause)
		{
			Log.Warn(TAG, "onConnectionSuspended:" + cause);
		}

		class DialogInterfaceOnCancelListener : Java.Lang.Object, IDialogInterfaceOnCancelListener
		{
			public Action<IDialogInterface> OnCancelImpl { get; set; }

			public void OnCancel(IDialogInterface dialog)
			{
				OnCancelImpl(dialog);
			}
		}

		void ShowErrorDialog(ConnectionResult connectionResult)
		{
			int errorCode = connectionResult.ErrorCode;
			var googleApi = GoogleApiAvailability.Instance;

			if (googleApi.IsUserResolvableError(errorCode))
			{
				var listener = new DialogInterfaceOnCancelListener
				{
					OnCancelImpl = (dialog) =>
					{
						ShouldResolve = false;
						UpdateUI(false);
					}
				};
				googleApi.GetErrorDialog(this, errorCode, RC_SIGN_IN, listener).Show();
			}
			else
			{
				var errorstring = string.Format(GetString(Resource.String.play_services_error_fmt), errorCode);
				Toast.MakeText(this, errorstring, ToastLength.Short).Show();

				ShouldResolve = false;
				UpdateUI(false);
			}
		}

		public async void OnClick(View v)
		{
			switch (v.Id)
			{
				case Resource.Id.sign_in_button:
					ShouldResolve = true;
					GoogleApiClient.Connect();
					var signInIntent = Auth.GoogleSignInApi.GetSignInIntent(GoogleApiClient);
					StartActivityForResult(signInIntent, RC_SIGN_IN);
					break;
				case Resource.Id.sign_out_button:
					if (GoogleApiClient.IsConnected)
					{
						await GoogleApiClient.ClearDefaultAccountAndReconnect();
						GoogleApiClient.Disconnect();
					}
					UpdateUI(false);
					break;
			}
		}
	}
}

