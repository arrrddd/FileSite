# FileSite

FileSite is a web-based file hosting service that allows users to upload files, set expiration times, and share them via a unique link. It supports both anonymous and registered user uploads.
Please dont upload copyrighted content if you are hosting the site it may and will cause you issues down the line.

The project is highly modular so its easily modifiably if wanted.
## Features

-   **File Upload:** Upload files through a simple web interface.
-   **File Expiration:** Set a lifetime for files (e.g., 1 day, 1 week, 1 month), after which they are automatically deleted.
-   **User Accounts:** Supports user registration and tracks which files belong to which user.
-   **Duplicate Detection:** Prevents uploading the same file multiple times by checking its MD5 hash.
-   **Efficient Storage:** Calculates file hash and size in a single pass while writing to disk.
-   **Mail Servicing:** Able to send or recieve emails for proccessing and even password changing
## Technologies Used

-   **.NET / ASP.NET Core:** The core web framework.
-   **C#:** The primary programming language.
-   **PostgreSQL:** The database for storing file metadata and user information.
-   **Redis:** Used for managing the expiration queue of temporary files.
-   **Nginx:** As a reverse proxy.
-   **Docker:** For containerizing the application and its services.
-   **Serilog(?):** For structured logging.

## Getting Started (Local Development)

### Prerequisites

-   [.NET SDK](https://dotnet.microsoft.com/download) (Check the project's `.csproj` for the specific version)
-   [PostgreSQL](https://www.postgresql.org/download/)
-   [Redis](https://redis.io/download/)

### Setup

1.  **Clone the repository:**
    ```bash
    git clone <your-repository-url>
    cd FileSite
    ```

2.  **Configure application settings:**
    Open `FileSite/appsettings.json` and update the connection strings for your local environment:
    ```json
    "ConnectionStrings": {
      "DefaultConnection": "Host=localhost;Port=5432;Database=filesite_db;Username=your_username;Password=your_password",
      "Redis": "localhost:6379"
    }
    ```

3.  **Apply database migrations:**
    Run the following command from the `FileSite` project directory to create the database schema.
    ```bash
    dotnet ef database update
    ```

4.  **Run the application:**
    ```bash
    dotnet run
    ```
    The application should now be running and accessible at `https://localhost:5001` or a similar address.

## Getting Started (Docker with Nginx)

This is the recommended way to run the application in a production-like environment.

### Prerequisites

-   [Docker](https://www.docker.com/get-started)
-   [Docker Compose](https://docs.docker.com/compose/install/)

### Setup

1.  **Clone the repository:**
    ```bash
    git clone <your-repository-url>
    cd FileSite
    ```

2.  **Run with Docker Compose:**
    From the root directory (the one containing `docker-compose.yml`), run the following command:
    ```bash
    docker-compose up -d
    ```
    This will build the application image, start containers for the web app, Nginx, PostgreSQL, and Redis, and configure them to work together.

3.  **Access the application:**
    The application will be accessible at `http://localhost:8080`.

## Project Structure

-   **/FileSite/Data:** Contains the `ApplicationDbContext`, interfaces (`IDiskStorageRepository`), and EF Core configurations.
-   **/FileSite/Models:** Contains the domain models (`FileData`, `AppUser`).
-   **/FileSite/Repositories:** Contains the data access logic (`FileDataRepository`, `DiskStorageRepository`).
-   **/FileSite/Services:** Contains background services (`FileCleanup`) and other business logic.
-   **/FileSite/Views:** Contains the CSHTML files for the user interface.
-   **/FileSite/Controllers:** Contains the MVC controllers that handle web requests.
-   **/FileSiteTesting:** Contains unit and integration tests for the project.
-   **docker-compose.yml:** Orchestrates all the services for a production-like environment.
-   **/FileSite/Dockerfile:** Defines how to build the application's container image.
-   **/FileSite/nginx.conf:** Nginx configuration to act as a reverse proxy.
