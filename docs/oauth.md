# Signing in through the browser

GitHub Desktop signs in through the browser because GitHub knows it as a registered app. Urras works the same way
once it is registered: one registration covers everyone who uses that copy of Urras. Without it, Urras asks for a
personal access token instead.

## GitHub

1. Open GitHub, then Settings > Developer settings > OAuth Apps > New OAuth App.
2. Application name: `Urras`. Homepage URL: the Urras repository page. Authorization callback URL: the same page
   (the device flow doesn't use it, but GitHub asks for one).
3. Register the application, then tick **Enable Device Flow** and save.
4. Copy the **Client ID**. It isn't a secret: no client secret is needed or used.
5. Either paste it in Urras under Options > Accounts > GitHub client id, or set `GITHUB_CLIENT_ID` in
   `src/Urras.Core/Accounts/DeviceSignIn.cs` so every build signs in through the browser.

For GitHub Enterprise Server, register the app on that server the same way and paste its client id.

## GitLab

1. Open GitLab, then Preferences > Applications > Add new application (or Admin > Applications on your own server).
2. Name: `Urras`. Redirect URI: the Urras repository page. Untick **Confidential**.
3. Scopes: `api` and `read_user`. Save.
4. Copy the **Application ID**, and paste it under Options > Accounts > GitLab application id, or set
   `GITLAB_CLIENT_ID` in `DeviceSignIn.cs` (that one is only used for gitlab.com).

## Other platforms

Gitea, Forgejo, Codeberg and Bitbucket don't offer the device flow, so they keep using tokens. Bitbucket also
accepts `username:app password`.
