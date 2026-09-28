const jwt = require("jsonwebtoken");

function generateToken(user) {
    const secret = process.env.JWT_SECRET;

    if (!secret) {
        throw new Error("JWT_SECRET is not configured");
    }

    return jwt.sign(
        {
            sub: String(user.id),
            username: user.username,
            role: user.role
        },
        secret,
        {
            expiresIn: process.env.JWT_EXPIRES_IN || "1h",
            issuer: "nexus-auth"
        }
    );
}

module.exports = {
    generateToken
};