const express = require("express");
const db = require("./db");
const authService = require("./services/authService");
const tokenService = require("./services/tokenService");

const app = express();
const PORT = 3000;

app.use(express.json());

app.get("/health", (req, res) => {
    res.status(200).json({
        status: "healthy",
        service: "nexus-auth"
    });
});

app.get("/health/database", async (req, res) => {
    try {
        const result = await db.query(
            "SELECT current_database()"
        );

        res.status(200).json({
            status: "healthy",
            database: result.rows[0].current_database
        });
    } catch (error) {
        console.error(
            "Database health check failed:",
            error.message
        );

        res.status(500).json({
            status: "unhealthy",
            database: "unavailable"
        });
    }
});

app.post("/auth/register", async (req, res) => {
    try {
        const {
            username,
            email,
            password,
            role
        } = req.body;

        if (!username || !email || !password) {
            return res.status(400).json({
                error: "Username, email and password are required"
            });
        }

        const user = await authService.registerUser({
            username,
            email,
            password,
            role
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

        console.error(
            "Registration failed:",
            error.message
        );

        return res.status(500).json({
            error: "Registration failed"
        });
    }
});

app.post("/auth/login", async (req, res) => {
    try {
        const {
            username,
            password
        } = req.body;

        if (!username || !password) {
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
        console.error(
            "Authentication failed:",
            error.message
        );

        return res.status(500).json({
            error: "Authentication failed"
        });
    }
});

db.connect()
    .then(() => {
        console.log("Connected to PostgreSQL");

        app.listen(PORT, () => {
            console.log(
                `NEXUS Auth Service listening on http://localhost:${PORT}`
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