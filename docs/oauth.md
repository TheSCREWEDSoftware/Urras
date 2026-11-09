# Signing in through the browser

GitHub Desktop signs in through the browser because GitHub knows it as a registered app. Urras works the same way:
it is registered on github.com, and its client id is built in (`GITHUB_CLIENT_ID` in `DeviceSignIn.cs`), so GitHub
sign-in goes through the browser with nothing to set up. A client id is public by design; there is no secret.

The steps below are for registering your own copy, a GitHub Enterprise server or GitLab. Without a registration,
Urras asks for a personal access token instead.

## GitHub

1. Open GitHub, then Settings > Developer settings > OAuth Apps > New OAuth App (for an organization, its own
   Settings > Developer settings, so people see the organization's name when they sign in).
2. Application name: `Urras`. Homepage URL: the Urras repository page or its GitHub Pages site. Redirect URI: the
   same address (the device flow doesn't use it, but GitHub asks for one); leave **Allow wildcard matching** off.
3. Tick **Enable Device Flow**. **Expire user access tokens** can stay off; if it's on, Urras renews the token
   with the refresh token GitHub gives it, before it runs out.
   Then register the application (on older forms, tick Enable Device Flow on the page that follows and save).
4. Copy the **Client ID**. It isn't a secret: no client secret is needed or used.
5. Either paste it in Urras under Options > Accounts > GitHub client id, or set `GITHUB_CLIENT_ID` in
   `src/Urras.Core/Accounts/DeviceSignIn.cs` so every build signs in through the browser.

For GitHub Enterprise Server, register the app on that server the same way and paste its client id.

## GitLab

GitLab's access tokens expire after two hours. Urras keeps the refresh token GitLab gives it and renews the
access token a few minutes before it runs out, so you stay signed in.

1. Open GitLab, select your avatar > Edit profile, then in the left sidebar **Access > Applications** (Access opens
   into a submenu), and Add new application. On your own server, Admin > Applications works too.
2. Name: `Urras`. Redirect URI: the Urras repository page. Untick **Confidential**.
3. Scopes: `api` and `read_user`. Save.
4. Copy the **Application ID**, and paste it under Options > Accounts > GitLab application id, or set
   `GITLAB_CLIENT_ID` in `DeviceSignIn.cs` (that one is only used for gitlab.com).

## Other platforms

Gitea, Forgejo, Codeberg and Bitbucket don't offer the device flow, so they keep using tokens. Bitbucket also
accepts `username:app password`.
