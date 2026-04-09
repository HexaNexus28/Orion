---

## Audit et Traçabilité

### Service d'Audit (IAuditService)

Le service d'audit fournit une traçabilité complète des actions dans ORION.

```csharp
// Log manuel
await _auditService.LogAsync(
    entityType: "Conversation",
    entityId: conversation.Id.ToString(),
    action: "Create",
    newValues: JsonSerializer.Serialize(conversation),
    metadata: "{ \"ip\": \"127.0.0.1\" }"
);

// Log automatique pour entités
await _auditService.LogEntityCreateAsync(conversation);
await _auditService.LogEntityUpdateAsync(oldConv, newConv);
await _auditService.LogEntityDeleteAsync(conversation);

// Log pour Tools
await _auditService.LogToolCallAsync(
    toolName: "get_shiftstar_stats",
    input: jsonInput,
    output: jsonOutput,
    duration: stopwatch.Elapsed,
    success: true
);

// Log pour LLM
await _auditService.LogLLMCallAsync(
    provider: "Ollama",
    model: "kimi-k2",
    prompt: userMessage,
    response: aiResponse,
    tokensUsed: 150,
    duration: TimeSpan.FromSeconds(2.5)
);
```

### Table audit_logs

| Champ | Description |
|-------|-------------|
| **entity_type** | Type d'entité concernée (Conversation, Message, Tool, LLM) |
| **entity_id** | ID de l'entité |
| **action** | Action effectuée (Create, Update, Delete, ToolCall, LLMCall) |
| **correlation_id** | Lien entre actions d'une même requête |
| **duration_ms** | Temps d'exécution |
| **success** | Succès ou échec |

### Correlation ID

Le Correlation ID permet de lier toutes les actions d'une même requête :

```csharp
// Définir au début de la requête
_auditService.SetCorrelationId(Guid.NewGuid().ToString());

// Récupérer automatiquement dans les logs
var correlationId = _auditService.GetCorrelationId();
```

### Flux avec Audit

```
1. Controller reçoit requête
   └─> Définit CorrelationId
       
2. Service exécute logique
   └─> Appelle AuditService.LogAsync()
       
3. Agent exécute action
   └─> Appelle AuditService.LogToolCallAsync() ou LogLLMCallAsync()
       
4. Repository persiste
   └─> Appelle AuditService.LogEntityCreate/Update/DeleteAsync()
       
5. Tous les logs partagent le même CorrelationId
```

### Règles

- **Jamais** faire échouer une opération à cause de l'audit
- **Toujours** utiliser try/catch dans les méthodes d'audit
- **Toujours** loguer les erreurs d'audit (mais ne pas propager)
- Utiliser `AsyncLocal` pour le CorrelationId dans les contextes async
