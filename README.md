# Chat to RAG to Agents

> A milestone-based, local-first learning journey for building a modern LLM-powered chatbot—from basic text chat to tools, retrieval-augmented generation, quality controls, memory, and early multi-agent patterns.

![LLM Chatbot Learning Roadmap](docs/assets/llm-chatbot-learning-roadmap.png)

## Quick Start

Use these commands for a first run on Windows or macOS.

Windows (PowerShell):

	winget install Microsoft.DotNet.SDK.10
	winget install OpenJS.NodeJS.LTS
	winget install Ollama.Ollama
	dotnet restore Chatbot.sln
	cd frontend/chatbot-ui && npm install && cd ../..
	node scripts/setup-ollama.mjs
	node scripts/start-dev-stack.mjs

macOS (Homebrew):

	brew install --cask dotnet-sdk
	brew install node
	brew install ollama
	dotnet restore Chatbot.sln
	cd frontend/chatbot-ui && npm install && cd ../..
	node scripts/setup-ollama.mjs
	node scripts/start-dev-stack.mjs

Open:

1. Frontend: http://localhost:4200
2. API Swagger: http://localhost:5273/swagger

Stop everything:

	node scripts/stop-dev-stack.mjs

## Overview

This repository demonstrates how an LLM-powered chatbot can evolve through small, reviewable milestones.

The journey begins with a basic locally hosted chatbot and progressively introduces capabilities commonly required in modern AI-enabled products:

* Streaming responses
* Markdown answer rendering
* Text-file and image context
* Model-selected tool calling
* Public and private API integrations
* Multi-tool orchestration
* Human approval for sensitive operations
* Tool retries, error handling, and observability
* Knowledge Hubs
* Document chunking and vectorization
* Qdrant-backed vector search
* Retrieval-augmented generation
* Retrieval tuning and evaluation
* Citations and grounding checks
* Conversation memory
* Prompt and ingestion controls
* Basic agent routing and handoff

The objective is not only to build a chatbot. It is to understand how each capability changes the product architecture, user experience, reliability model, and engineering responsibilities.

## Learning Roadmap

| Phase | Phase Definition | Milestone | Capability | Learning Guide | Status |
| --- | --- | --- | --- | --- | --- |
| Phase 1 - Foundation | Build the smallest useful chatbot and improve responsiveness with streaming and cancellation. | 01 | Basic Text Chat | [Learning Guide 01](docs/learning-guide-01-foundation-chat-and-streaming.md) | Published |
|  |  | 02 | Streaming Responses | [Learning Guide 01](docs/learning-guide-01-foundation-chat-and-streaming.md) | Published |
| Phase 2 - Rich Context | Add one-off text, code, structured files, and image context to chat requests. | 03 | Text File Context | TBD | Planned |
|  |  | 04 | Image Context | TBD | Planned |
| Phase 3 - Tool-Enabled Assistant | Move beyond text generation by allowing model-selected structured tool usage. | 05 | Tool Calling | TBD | Planned |
|  |  | 06 | External Tools | TBD | Planned |
|  |  | 07 | Private Business API Tools | TBD | Planned |
|  |  | 08 | Multi-Tool Calling | TBD | Planned |
| Phase 4 - Safety, Reliability, and Control | Introduce approval boundaries, retries, observability, and generation controls. | 09 | Human Approval Tools | TBD | Planned |
|  |  | 10 | Tool Errors and Retries | TBD | Planned |
|  |  | 11 | Observability | TBD | Planned |
|  |  | 12 | Model Generation Settings | TBD | Planned |
| Phase 5 - Knowledge Hubs and Retrieval | Create reusable document collections, chunk/vectorize content, and inspect semantic search. | 13 | Document Vectorization | TBD | Planned |
|  |  | 14 | Qdrant Vector Search | TBD | Planned |
| Phase 6 - Core RAG Experience | Connect retrieval to chat, inspect context, evaluate retrieval, and add citations. | 15 | Basic RAG Chat | TBD | Planned |
|  |  | 16 | RAG Observability | TBD | Planned |
|  |  | 17 | RAG Tuning Playground | TBD | Planned |
|  |  | 18 | RAG Evaluation Basics | TBD | Planned |
|  |  | 19 | RAG Debug Inspector | TBD | Planned |
|  |  | 20 | Document Management Improvements | TBD | Planned |
|  |  | 21 | RAG Citations | TBD | Planned |
| Phase 7 - RAG Quality, Memory, and Tuning | Improve recall, ranking, grounding, citation quality, follow-up memory, and prompt control. | 22 | RAG Query Rewriting | TBD | Planned |
|  |  | 23 | RAG Reranking | TBD | Planned |
|  |  | 24 | RAG Hybrid Search | TBD | Planned |
|  |  | 25 | RAG Query Expansion | TBD | Planned |
|  |  | 26 | RAG Answer Grounding Check | TBD | Planned |
|  |  | 27 | RAG Citation Quality | TBD | Planned |
|  |  | 28 | RAG Conversation Memory | TBD | Planned |
|  |  | 29 | RAG Prompt Templates | TBD | Planned |
|  |  | 30 | RAG Ingestion Improvements | TBD | Planned |
| Phase 8 - Early Multi-Agent Patterns | Introduce specialist routing, transparent planning, and controlled handoff. | 31 | Basic Multi-Agent Router | TBD | Planned |
|  |  | 32 | Agent Planning and Handoff | TBD | Planned |

## What You Will Learn

By following the milestones, you will learn how to:

* Build a full-stack LLM chat experience
* Stream model responses using Server-Sent Events
* Handle text and image context
* Render model output safely as Markdown
* Define and invoke structured tools
* Integrate public and private APIs
* Orchestrate multiple tool calls
* Introduce human approval for sensitive operations
* Capture tool failures, retries, timings, and outputs
* Tune model-generation settings
* Create reusable document collections
* Chunk, embed, and vectorize documents
* Perform semantic search using Qdrant
* Build and inspect a RAG pipeline
* Tune retrieval behavior
* Evaluate retrieval quality
* Add citations and grounding checks
* Support conversational follow-up questions
* Route requests to specialist agent profiles
* Expose planning and handoff metadata without exposing hidden model reasoning

## Technology Stack

| Area                 | Technology                                                            |
| -------------------- | --------------------------------------------------------------------- |
| Frontend             | Angular, Angular Material, Markdown rendering                         |
| Backend              | ASP.NET Core, .NET                                                    |
| Local model hosting  | Ollama with Gemma                                                     |
| Streaming            | Server-Sent Events                                                    |
| Vector database      | Qdrant                                                                |
| Testing              | Backend unit tests, frontend unit tests, Playwright                   |
| Development workflow | VS Code tasks and local scripts                                       |
| Documentation        | Markdown guides, Git tags, releases, screenshots, and learning assets |

The application is designed around interfaces for model and vector-store access.

Ollama and Qdrant are used for the local tutorial, but the application boundaries are intended to support future providers without requiring a complete rewrite of the product experience.

## Run the Application Locally

### Prerequisites

Install the following tools before running the app:

1. .NET SDK 10 (required for backend projects targeting net10.0)
2. Node.js version 22.22.3 or newer (or Node 24.15.0+)
3. npm (comes with Node.js)
4. Ollama

Recommended on Windows using winget:

	winget install Microsoft.DotNet.SDK.10
	winget install OpenJS.NodeJS.LTS
	winget install Ollama.Ollama

Recommended on macOS using Homebrew:

	brew install --cask dotnet-sdk
	brew install node
	brew install ollama

Verify installation:

	dotnet --list-sdks
	node -v
	npm -v
	ollama --version

### Install Repository Dependencies

From the repository root:

	dotnet restore Chatbot.sln
	cd frontend/chatbot-ui
	npm install
	cd ../..

### Set Up Ollama and Models

The repository includes a setup script that:

1. Installs Ollama if it is missing (Windows)
2. Starts Ollama if it is not running
3. Pulls configured models if they are not present

Run from repository root:

	node scripts/setup-ollama.mjs

By default, it checks the model configured in backend/src/Chatbot.Api/appsettings.json and also pulls the embedding model used in later milestones.

You can also provide models explicitly:

	node scripts/setup-ollama.mjs gemma3:4b nomic-embed-text:latest

### Start the Full Development Stack

From the repository root:

	node scripts/start-dev-stack.mjs

This starts:

1. Ollama (if needed)
2. Backend API on http://localhost:5273
3. Angular frontend on http://localhost:4200

Open:

1. Frontend: http://localhost:4200
2. API Swagger: http://localhost:5273/swagger

To stop all started processes:

	node scripts/stop-dev-stack.mjs

### Start Services Manually (Optional)

If you prefer separate terminals:

Terminal 1 (Ollama):

	ollama serve

Terminal 2 (Backend):

	dotnet run --project backend/src/Chatbot.Api/Chatbot.Api.csproj --launch-profile http

Terminal 3 (Frontend):

	cd frontend/chatbot-ui
	npm start

### Common Troubleshooting

1. Error NETSDK1045 (targeting net10.0 with older SDK)
   Install .NET 10 SDK and confirm it appears in dotnet --list-sdks.

2. Angular CLI Node version error
   Upgrade Node.js to a supported version (for example 24.15.0).

3. Ollama model not found
   Pull the configured model in backend/src/Chatbot.Api/appsettings.json, for example:

	   ollama pull gemma3:4b

4. First message works, later messages time out
   Increase Ollama request timeout in backend/src/Chatbot.Api/appsettings.json:

	   "Ollama": {
		 "BaseUrl": "http://localhost:11434",
		 "ChatModel": "gemma3:4b",
		 "RequestTimeoutSeconds": 300
	   }



---

If this repository helps you understand how modern LLM applications evolve from chat to RAG and agents, consider starring the repository and sharing what you learned.
