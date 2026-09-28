# NEXUS Service Architecture

## Overview

NEXUS currently consists of two independently executable backend services:

- ASP.NET Core API
- Node.js Authentication Service

Each service runs as its own process and exposes a health endpoint.

## Services

| Service | Technology | Port | Health Endpoint |
|---|---|---:|---|
| NEXUS API | ASP.NET Core / .NET 10 | 5160 | `/health` |
| NEXUS Auth | Node.js / Express | 3000 | `/health` |

## Current Architecture

```text
                    NEXUS
                      |
             +--------+--------+
             |                 |
             v                 v
       ASP.NET Core        Node.js / Express
          API               Auth Service
         :5160                :3000
           |                    |
           v                    v
       GET /health          GET /health
           |                    |
        200 OK                200 OK