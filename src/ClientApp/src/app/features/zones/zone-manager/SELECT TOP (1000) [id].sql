-- Create the login (if it doesn't exist)
CREATE LOGIN appUser WITH PASSWORD = 'Password2';

-- Switch to the signals database
USE signals;

-- Create the user mapped to the login
CREATE USER appUser FOR LOGIN appUser;

-- Grant permissions
ALTER ROLE db_owner ADD MEMBER appUser;
