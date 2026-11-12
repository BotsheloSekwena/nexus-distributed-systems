const express = require("express");
const helmet = require("helmet");
const { rateLimit } = require("express-rate-limit");
const crypto = require("crypto");

const db = require("./db");
const authService = require("./services/authService");
const tokenService = require("./services/tokenService");

const app = express();
const PORT = 3000;

const internalApiKey = process.env.AUTH_INTERNAL_API_KEY;

if (!internalApiKey) {
    throw new Error("AUTH_INTERNAL_API_KEY is not configured");
}

// Traefik is the single trusted reverse-proxy hop.
app.set("trust proxy", 1);

// Apply HTTP security headers.
app.use(helmet());

// Restrict incoming JSON request size.
app.use(express.json({ limit: "10kb" }));

/**
 * Limits repeated login attempts from a client IP.
 */
const loginLimiter = rateLimit({
    windowMs: 15 * 60 * 1000,
    limit: 5,
    standardHeaders: true,
    legacyHeaders: false,
    message: {
        error: "Too many login attempts. Please try again later."
    }
});

// Health check.
app.get("/health", (req, res) => {
    res.status(200).json({
        status: "healthy",
        service: "nexus-auth"
    });
});

// Database health check.
app.get("/health/database", async (req, res) => {
    try {
        const result = await db.query("SELECT current_database()");

        res.status(200).json({
            status: "healthy",
            database: result.rows[0].current_database
        });
    } catch (error) {
        console.error("Database health check failed:", error.message);

        res.status(500).json({
            status: "unhealthy",
            database: "unavailable"
        });
    }
});

// Internal token validation endpoint.
// This route is intended for service-to-service communication only.
app.post("/internal/tokens/validate", async (req, res) => {
    try {
        const providedKey = req.get("X-Internal-Api-Key");

        if (typeof providedKey !== "string") {
            return res.status(401).json({
                valid: false
            });
        }

        const expectedBuffer = Buffer.from(internalApiKey);
        const providedBuffer = Buffer.from(providedKey);

        const validApiKey =
            expectedBuffer.length === providedBuffer.length &&
            crypto.timingSafeEqual(expectedBuffer, providedBuffer);

        if (!validApiKey) {
            return res.status(401).json({
                valid: false
            });
        }

        const { userId, tokenVersion, role } = req.body ?? {};

        if (
            typeof userId !== "string" ||
            !/^[1-9]\d*$/.test(userId) ||
            !Number.isSafeInteger(tokenVersion) ||
            tokenVersion < 0 ||
            typeof role !== "string"
        ) {
            return res.status(400).json({
                error: "Invalid token validation request"
            });
        }

        const valid = await authService.validateToken({
            userId,
            tokenVersion,
            role
        });

        if (!valid) {
            return res.status(401).json({
                valid: false
            });
        }

        return res.status(200).json({
            valid: true
        });
    } catch (error) {
        console.error("Internal token validation failed:", error.message);

        return res.status(503).json({
            error: "Token validation service unavailable"
        });
    }
});

// User registration.
app.post("/auth/register", async (req, res) => {
    try {
        const { username, email, password } = req.body ?? {};

        if (
            typeof username !== "string" ||
            typeof email !== "string" ||
            typeof password !== "string"
        ) {
            return res.status(400).json({
                error: "Username, email and password are required"
            });
        }

        const normalizedUsername = username.trim();
        const normalizedEmail = email.trim();

        if (
            normalizedUsername.length < 3 ||
            normalizedUsername.length > 100
        ) {
            return res.status(400).json({
                error: "Username must contain between 3 and 100 characters"
            });
        }

        if (
            normalizedEmail.length > 255 ||
            !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(normalizedEmail)
        ) {
            return res.status(400).json({
                error: "A valid email address is required"
            });
        }

        const passwordBytes = Buffer.byteLength(password, "utf8");

        if (password.length < 12 || passwordBytes > 72) {
            return res.status(400).json({
                error: "Password must be at least 12 characters and no more than 72 UTF-8 bytes"
            });
        }

        const user = await authService.registerUser({
            username: normalizedUsername,
            email: normalizedEmail,
            password
        });

        return res.status(201).json({
            message: "User registered successfully",
            user
        });
    } catch (error) {
        if (
            error.message === "Username already exists" ||
            error.message === "Email already exists"
        ) {
            return res.status(409).json({
                error: error.message
            });
        }

        // Handle concurrent registrations rejected by PostgreSQL.
        if (error.code === "23505") {
            return res.status(409).json({
                error: "Username or email already exists"
            });
        }

        console.error("Registration failed:", error.message);

        return res.status(500).json({
            error: "Registration failed"
        });
    }
});

// User login.
app.post("/auth/login", loginLimiter, async (req, res) => {
    try {
        const { username, password } = req.body ?? {};

        if (
            typeof username !== "string" ||
            typeof password !== "string" ||
            !username.trim() ||
            !password
        ) {
            return res.status(400).json({
                error: "Username and password are required"
            });
        }

        const user = await authService.authenticateUser(
            username,
            password
        );

        if (!user) {
            return res.status(401).json({
                error: "Invalid username or password"
            });
        }

        const token = tokenService.generateToken(user);

        return res.status(200).json({
            message: "Authentication successful",
            token,
            user
        });
    } catch (error) {
        console.error("Authentication failed:", error.message);

        return res.status(500).json({
            error: "Authentication failed"
        });
    }
});

// Handle malformed JSON and oversized requests.
app.use((error, req, res, next) => {
    if (error.type === "entity.too.large") {
        return res.status(413).json({
            error: "Request body is too large"
        });
    }

    if (error instanceof SyntaxError && "body" in error) {
        return res.status(400).json({
            error: "Invalid JSON request body"
        });
    }

    console.error("Unhandled request error:", error.message);

    return res.status(500).json({
        error: "Internal server error"
    });
});

// Start the Auth service after establishing its database connection.
db.connect()
    .then(() => {
        console.log("Connected to PostgreSQL");

        app.listen(PORT, () => {
            console.log(
                `NEXUS Auth Service listening on port ${PORT}`
            );
        });
    })
    .catch((error) => {
        console.error(
            "Failed to connect to PostgreSQL:",
            error.message
        );

        process.exit(1);
    });