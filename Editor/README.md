### Neovim code editor for Unity

This project aims to add a flexible cross-platform support for nvim as a code editor with unity.

Project goals:
* Cross-platform support (Windows, Linux, OSX)
* Flexibility - Ready to use with any nvim client you want.
* Project generation - Generates .sln files just like the vscode extension for use with LSP.

Besides the editor itself, the plugin requires nvr to be installed so we can pass commands to a running nvim instance.
It could in theory be made to work directly using nvim --remote commands, but due to lack of proper documentation and examples, I was not able to get it to work.

# How it works
* First, we are spawning a nvim instance as a server in the background, it listens for connections on a pipe. 
The pipe name contains a hash of the same project directory, so you will only have a single nvim instance running a given project.
* Then, we issue a command via nvr to open a given file at the desired line.
* If we haven't previously launched a client process, we start a new process with the desired client and attach to the running nvim server.


## Neovim config

Configuring your nvim to be compatible with unity is out of scope for this project, but here is my experience:

At the time of writing, I'm running nvim 0.9.1 and basic lazyvim.
I have only omnisharp-mono installed using mason, and it provides great code actions and completion.
For proper detection of newly-added files, the following config is required for your LSP (Credits go to [@niscolas](https://github.com/niscolas)) for figuring it out:

Your LSP Capabilities table should include `workspace.didChangeWatchedFiles.dynamicRegistration = true (Neovim news.txt)`, example:

```lua
local cmp_nvim_lsp = require("cmp_nvim_lsp")
local capabilities = cmp_nvim_lsp.default_capabilities()
capabilities = vim.tbl_deep_extend("force", capabilities, {
    workspace = {
        didChangeWatchedFiles = {
            dynamicRegistration = true,
        },
    },
})
// pass `capabilities` to your LSP server `setup()`
```

