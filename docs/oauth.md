# Signing in through the browser

GitHub Desktop signs in through the browser because GitHub knows it as a registered app. Urras works the same way:
it is registered on github.com, and its client id is built in (`GITHUB_CLIENT_ID` in `DeviceSignIn.cs`), so GitHub
sign-in goes through the browser with nothing to set up. A client id is public by design; there is no secret.

The same goes for gitlab.com: Urras is registered there too and its application id is built in
(`GITLAB_CLIENT_ID`). The steps below are for registering your own copy, a GitHub Enterprise server or your own
GitLab server. Without a registration, Urras asks for a personal access token instead.

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

Browser sign-in uses GitLab's device flow, which gitlab.com has; a server of your own needs GitLab 17.3 or later.

1. Open <https://gitlab.com/-/user_settings/applications> while signed in (on your own server,
   `https://your-server/-/user_settings/applications`, or Admin > Applications for the whole server). Through the
   menus it is your avatar > Edit profile > Applications in the left sidebar.
2. Select **Add new application**.
3. Name: `Urras`. Redirect URI: `https://thescrewedsoftware.github.io/Urras/` (GitLab asks for one; the device
   flow never opens it).
4. **Untick Confidential.** It is ticked to start with, and a confidential application needs a secret, which a
   desktop app can't keep; GitLab then refuses with "Client authentication failed".
5. **Tick Device authorization grant.** Without it GitLab won't hand out the code Urras shows in the browser.
6. Scopes: tick **api** and **read_user**, both, and nothing else is needed. Urras asks for exactly these, and
   GitLab refuses the sign-in ("invalid_scope") when either is missing. `api` also covers pushing and pulling
   over HTTPS, which Urras does with the same sign-in.
7. Select **Save application**, then copy the **Application ID** (not the Secret).
8. In Urras, paste it under Options > Accounts > GitLab application id and save (or set `GITLAB_CLIENT_ID` in
   `DeviceSignIn.cs`, used for gitlab.com), then add the GitLab account from Accounts; the browser opens with a
   code to confirm.

If something is off, Urras names it in the status line, such as the Confidential box, the device authorization
grant or a missing scope, and offers a personal access token instead.

## Other platforms

Gitea, Forgejo, Codeberg and Bitbucket don't offer the device flow, so they keep using tokens. Bitbucket also
accepts `username:app password`.
