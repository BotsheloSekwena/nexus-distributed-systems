const express = require("express");

const app = express();
const PORT = 3000;

app.use(express.json());

app.get("/health", (req, res) => {
    res.status(200).json({
        status: "healthy",
        service: "nexus-auth"
    });
});

app.listen(PORT, () => {
    console.log(`NEXUS Auth Service listening on http://localhost:${PORT}`);
});