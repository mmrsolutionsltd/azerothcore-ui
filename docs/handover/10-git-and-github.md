# Git and GitHub access

Repository owner/account: `mmrsolutionsltd`

Repository: `https://github.com/mmrsolutionsltd/azerothcore-ui.git`

Default branch: `master`

Local commit identity: `markr <mmrsolutionsltd@gmail.com>`

The Windows Git credential helper is Git Credential Manager (`manager`). Verify locally:

```powershell
git remote -v
git config --get user.name
git config --get user.email
git config --get credential.helper
```

GitHub HTTPS operations use a personal access token rather than an account password. Let Git Credential Manager prompt for it; never put tokens in URLs, scripts, handover documents, shell history, or logs.

```powershell
git fetch origin
git pull --ff-only origin master
git push origin master
```

An SSH remote is possible after registering a dedicated public key with GitHub:

```powershell
git remote set-url origin git@github.com:mmrsolutionsltd/azerothcore-ui.git
ssh -T git@github.com
git push origin master
```

The Linux `claude` key is for `azerothmedia`; it is not automatically a GitHub key. Use a separate deploy key or user key with only the required repository access.

Review staged changes before committing and never commit production JSON, passwords, private keys, database dumps, or generated release artifacts.
