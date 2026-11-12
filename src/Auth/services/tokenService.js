
const jwt = require("jsonwebtoken");

/**
 * Generates a signed JWT for an authenticated user.
 */
function generateToken(user) {
    const secret = process.env.JWT_SECRET;
    const issuer = process.env.JWT_ISSUER;
    const audience = process.env.JWT_AUDIENCE;

    if (!secret || !issuer || !audience) {
        throw new Error("JWT configuration is incomplete");
    }

    return jwt.sign(
        {
            sub: String(user.id),
            username: user.username,
            role: user.role,
            tokenVersion: user.tokenVersion
        },
        secret,
        {
            expiresIn: process.env.JWT_EXPIRES_IN || "1h",
            issuer,
            audience
        }
    );
}

module.exports = {
    generateToken
};