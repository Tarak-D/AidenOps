export async function getItsmProviderStatus() {
    const response = await fetch("/api/v1/integrations/itsm/status", {
        method: "GET",
        credentials: "same-origin",
        headers: { "Accept": "application/json" }
    });

    if (!response.ok) {
        throw new Error("ITSM provider status is unavailable.");
    }

    return await response.json();
}

export async function selectItsmProvider(provider, devIdentity) {
    const headers = { "Content-Type": "application/json", "Accept": "application/json" };
    if (devIdentity) {
        headers["X-Dev-User"] = devIdentity;
    }

    const response = await fetch("/api/v1/integrations/itsm/provider", {
        method: "POST",
        credentials: "same-origin",
        headers,
        body: JSON.stringify({ provider })
    });

    if (!response.ok) {
        throw new Error("The ITSM provider selection could not be saved.");
    }
}
