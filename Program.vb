Option Strict On
Option Infer On
Imports VbPixelGameEngine
Imports YamlDotNet.Serialization
Imports YamlDotNet.Serialization.NodeTypeResolvers
Imports NAudio.Wave
Imports JaggedListStr =
    System.Collections.Generic.List(Of System.Collections.Generic.List(Of String))

Public NotInheritable Class Program
    Inherits PixelGameEngine

    Private floor As Integer = 0, totalMoves As Integer = 0, levelMoves As Integer = 0
    Private levelProfiles As Dictionary(Of String, JaggedListStr)
    Private Const TILE_SIZE As Integer = 20
    Private levelOffset As Vi2d
    Private originalPlayerPos As Vi2d
    Private ReadOnly originalBoxes As New List(Of Vi2d)

    ' Game state variables
    Private playerPos As Vi2d
    Private ReadOnly boxes As New List(Of Vi2d)
    Private ReadOnly goals As New List(Of Vi2d)
    Private ReadOnly walls As New List(Of Vi2d)
    Private ReadOnly floorTiles As New List(Of Vi2d)
    Private levelCompleted As Boolean = False

    Public Class AudioSource
        Private ReadOnly reader As AudioFileReader
        Private ReadOnly waveOut As WaveOutEvent
        Private isLooping As Boolean = False

        Public Sub New(filename As String)
            reader = New AudioFileReader(filename)
            waveOut = New WaveOutEvent
            waveOut.Init(reader)
            ' This is the looping mechanism for the audio source, which will not be
            ' applied when the audio is played once.
            AddHandler waveOut.PlaybackStopped, AddressOf OnPlaybackStopped
        End Sub

        Public Sub PlayOnce()
            If waveOut IsNot Nothing Then
                isLooping = False
                reader.Position = 0
                waveOut.Play()
            End If
        End Sub

        Public Sub PlayLooping()
            If waveOut IsNot Nothing Then
                isLooping = True
                reader.Position = 0
                waveOut.Play()
            End If
        End Sub

        Public Sub [Stop]()
            If waveOut IsNot Nothing Then
                isLooping = False
                waveOut.Stop()
            End If
        End Sub

        Private Sub OnPlaybackStopped(sender As Object, e As StoppedEventArgs)
            If isLooping AndAlso waveOut IsNot Nothing Then
                reader.Position = 0
                waveOut.Play()
            End If
        End Sub
    End Class

    Public Sub New()
        AppName = "VBPGE Sokoban Game"
    End Sub

    Protected Overrides Function OnUserCreate() As Boolean
        SetPixelMode(Pixel.Mode.Mask)

        ' Load level data
        With (New DeserializerBuilder).WithoutNodeTypeResolver(Of TagNodeTypeResolver).Build()
            levelProfiles = .Deserialize(Of Dictionary(Of String, JaggedListStr))(
                IO.File.ReadAllText("Assets/LevelDesignData.yml")
            )
        End With

        LoadLevel(floor)
        Return True
    End Function

    ' Helper method to load level data
    Private Sub LoadLevel(level As Integer)
        bgmMainTheme.Stop()
        levelCompleted = False
        levelMoves = 0
        boxes.Clear()
        goals.Clear()
        walls.Clear()
        floorTiles.Clear()
        originalBoxes.Clear()

        Dim levelName = If(level = 0, "Opening", $"Floor {level}")
        Dim levelData = levelProfiles(levelName)

        ' Calculate level offset for centering
        Dim levelWidth = levelData(0).Count * TILE_SIZE
        Dim levelHeight = levelData.Count * TILE_SIZE
        levelOffset = New Vi2d(
            (ScreenWidth - levelWidth) \ 2,
            (ScreenHeight - levelHeight) \ 2
        )

        ' Parse level data
        For y As Integer = 0 To levelData.Count - 1
            For x As Integer = 0 To levelData(y).Count - 1
                Dim cell = levelData(y)(x)
                Dim pos = New Vi2d(x, y)

                Select Case cell
                    Case "w"
                        walls.Add(pos)
                    Case "b"
                        boxes.Add(pos)
                        originalBoxes.Add(pos) ' Store original position
                        floorTiles.Add(pos)
                    Case "g"
                        goals.Add(pos)
                    Case "B"   ' For boxes already on target, add them as goals too.
                        boxes.Add(pos)
                        originalBoxes.Add(pos)
                        goals.Add(pos)
                        floorTiles.Add(pos)
                    Case "p"
                        playerPos = pos
                        originalPlayerPos = pos ' Store original position
                        floorTiles.Add(pos)
                    Case Else
                        floorTiles.Add(pos)
                End Select
            Next x
        Next y

        ' Play appropriate music
        If level > 0 AndAlso elapsedDelay <= 0 Then bgmMainTheme.PlayLooping()
    End Sub

    ' Reset current level
    Private Sub ResetLevel()
        playerPos = originalPlayerPos
        boxes.Clear()
        boxes.AddRange(originalBoxes)
        levelMoves = 0
        levelCompleted = False
    End Sub

    Private ReadOnly Property TotalFloors As Integer
        Get
            Return levelProfiles.Keys.Count - 1
        End Get
    End Property

    Public ReadOnly Property MoveCountInfo(isVictory As Boolean) As String
        Get
            Dim rightHalf As String

            If isVictory Then
                rightHalf = If(totalMoves >= 1000, "At least 1000", $"{totalMoves,3}")
                Return $"Total moves taken: " & rightHalf
            Else
                rightHalf = If(totalMoves >= 1000, "Total >= 1000", $"Total = {totalMoves,3}")
                Return $"Moves = {levelMoves,3}" & New String(" "c, 3) & rightHalf
            End If
        End Get
    End Property

    Protected Overrides Function OnUserUpdate(elapsedTime As Single) As Boolean
        Clear(Presets.Black)

        ' Handle input
        If Not levelCompleted Then
            Dim moveDir As Vi2d = Nothing
            If GetKey(Key.UP).Pressed Then
                moveDir = New Vi2d(0, -1)
                playerSprite.LoadFromFile("Assets/player_up.png")
            End If
            If GetKey(Key.DOWN).Pressed Then
                moveDir = New Vi2d(0, 1)
                playerSprite.LoadFromFile("Assets/player_down.png")
            End If
            If GetKey(Key.LEFT).Pressed Then
                moveDir = New Vi2d(-1, 0)
                playerSprite.LoadFromFile("Assets/player_left.png")
            End If
            If GetKey(Key.RIGHT).Pressed Then
                moveDir = New Vi2d(1, 0)
                playerSprite.LoadFromFile("Assets/player_right.png")
            End If
            If GetKey(Key.R).Pressed Then ResetLevel()
            If moveDir <> Nothing Then HandleMovement(moveDir)
        End If

        ' Update delay timer
        If delayAction IsNot Nothing Then
            elapsedDelay -= elapsedTime * 1000 ' Convert to milliseconds
            If elapsedDelay <= 0 Then
                delayAction.Invoke()
                delayAction = Nothing
            End If
        End If

        ' Draw level layout, and clamp the level move count into 3 digits
        DrawLevelLayout()
        levelMoves = Math.Clamp(levelMoves, 0, 999)

        ' Check level completion
        If Not levelCompleted AndAlso CheckLevelComplete() Then
            bgmMainTheme.Stop()
            levelCompleted = True

            If floor = 0 Then
                ' Do not count moves at the opening level
                levelMoves = 0
                bgmGameStart.PlayOnce()
            Else
                bgmCleared.PlayOnce()
            End If

            ' Also limit the total moves into 3 digits
            totalMoves += levelMoves

            ' Advance to next level after delay
            TaskDelay(If(floor = 0, 7, 5),
                      Sub()
                          floor += 1
                          Dim levelKey = If(floor = 0, "Opening", $"Floor {floor}")
                          If levelProfiles.ContainsKey(levelKey) Then
                              LoadLevel(floor)
                          Else
                              ' Game completed - show victory screen
                              bgmVictory.PlayOnce()
                              floor = -1
                          End If
                      End Sub)
        End If

        Static firstLine As String, secondLine As String
        Select Case floor
            Case -1
                DrawString(10, 10, "CONGRATULATIONS!", Presets.Yellow, 2)
                DrawString(10, 30, "You've completed all levels!", Presets.Apricot)
                DrawString(10, 200, MoveCountInfo(True), Presets.Mint)
                DrawString(10, 220, "Press ""ESC"" to exit the game.", Presets.Lavender)
            Case 0
                ' Draw game hints at the bottom of the screen at the very start of the game.
                ' These hints only serve as guidance for novice players.
                DrawString(10, 195, "Arrow keys to move, ""R"" to restart the", Presets.White)
                DrawString(10, 210, "level, and ""ESC"" to exit at any time.", Presets.White)
                If levelCompleted Then
                    firstLine = "Great! Let's begin the adventure!"
                    secondLine = MoveCountInfo(False)
                Else
                    firstLine = "Welcome to the Sokoban game!"
                    secondLine = "Push the box to the target to begin."
                End If
            Case Else
                secondLine = MoveCountInfo(False)

                If floor = 6 AndAlso Not levelCompleted Then
                    ' Provide new hints for players at Floor 6, because levels after Floor 5
                    ' will make full use of position wrapping.
                    DrawString(10, 193, "NEW MECHANISM: SCREEN WRAPPING!", Presets.Cyan)
                    DrawString(10, 208, "Moving off an edge brings you to the", Presets.White)
                    DrawString(10, 223, "opposite side; the boxes too.", Presets.White)
                End If
                If levelCompleted AndAlso floor + 1 > TotalFloors Then
                    firstLine = "Excellent! Finishing the game."
                ElseIf levelCompleted Then
                    firstLine = $"Excellent! Heading to Floor {floor + 1}."
                Else
                    firstLine = $"You're now at Floor {floor} of {TotalFloors}"
                End If
        End Select
        If floor >= 0 Then
            DrawString(10, 10, firstLine, Presets.White)
            DrawString(10, 30, secondLine, Presets.White)
        End If

        Return Not GetKey(Key.ESCAPE).Pressed
    End Function

    Private Sub HandleMovement(moveDir As Vi2d)
        Dim profile = levelProfiles(If(floor = 0, "Opening", $"Floor {floor}"))
        Dim maxRowIdx = profile(0).Count - 1
        Dim maxColIdx = profile.Count - 1

        Dim WrapPosition = Function(input As Vi2d)
                               Dim wrapped As Vi2d = input
                               If wrapped.x < 0 Then wrapped.x = maxRowIdx
                               If wrapped.x > maxRowIdx Then wrapped.x = 0
                               If wrapped.y < 0 Then wrapped.y = maxColIdx
                               If wrapped.y > maxColIdx Then wrapped.y = 0
                               Return wrapped
                           End Function

        Dim newPlayerPos As Vi2d = playerPos + moveDir
        Dim wrappedPlayerPos = WrapPosition(newPlayerPos)

        ' Check if move is valid
        If walls.Contains(wrappedPlayerPos) Then Exit Sub

        ' Check if pushing a box
        Dim boxIndex = boxes.IndexOf(wrappedPlayerPos)
        If boxIndex >= 0 Then
            ' Calculate new box position relative to wrapped player position
            Dim newBoxPos As Vi2d = wrappedPlayerPos + moveDir
            Dim wrappedBoxPos = WrapPosition(newBoxPos)

            ' Can't push if blocked
            If walls.Contains(wrappedBoxPos) OrElse boxes.Contains(wrappedBoxPos) Then Exit Sub

            ' Move the box to wrapped position
            boxes(boxIndex) = wrappedBoxPos
            sfxBoxPushed.PlayOnce()
        End If

        ' Move player to wrapped position
        playerPos = wrappedPlayerPos
        levelMoves += 1
    End Sub

    Private Sub DrawLevelLayout()
        ' Draw floor tiles
        For Each pos As Vi2d In floorTiles
            DrawSprite(
                levelOffset.x + pos.x * TILE_SIZE,
                levelOffset.y + pos.y * TILE_SIZE,
                floorSprite
            )
        Next pos

        ' Draw goals
        For Each goal As Vi2d In goals
            DrawSprite(
                levelOffset.x + goal.x * TILE_SIZE,
                levelOffset.y + goal.y * TILE_SIZE,
                goalSprite
            )
        Next goal

        ' Draw walls
        For Each wall As Vi2d In walls
            DrawSprite(
                levelOffset.x + wall.x * TILE_SIZE,
                levelOffset.y + wall.y * TILE_SIZE,
                wallSprite
            )
        Next wall

        ' Draw boxes (on goals have different sprite)
        For Each box As Vi2d In boxes
            Dim sprite = If(goals.Contains(box), targetSprite, boxSprite)
            DrawSprite(
                levelOffset.x + box.x * TILE_SIZE,
                levelOffset.y + box.y * TILE_SIZE,
                sprite
            )
        Next box

        ' Draw player
        DrawSprite(
            levelOffset.x + playerPos.x * TILE_SIZE,
            levelOffset.y + playerPos.y * TILE_SIZE,
            playerSprite
        )
    End Sub

    Private Function CheckLevelComplete() As Boolean
        For Each goal In goals
            If Not boxes.Contains(goal) Then Return False
        Next
        Return True
    End Function

    ' Sprites
    Private ReadOnly playerSprite As New Sprite("Assets/player_right.png")
    Private ReadOnly boxSprite As New Sprite("Assets/peanut_box.png")
    Private ReadOnly targetSprite As New Sprite("Assets/box_on_target.png")
    Private ReadOnly wallSprite As New Sprite("Assets/obstacle.png")
    Private ReadOnly floorSprite As New Sprite("Assets/walkable.png")
    Private ReadOnly goalSprite As New Sprite("Assets/target.png")

    ' Audio
    Private ReadOnly bgmMainTheme As New AudioSource("Assets/main_theme.mp3")
    Private ReadOnly bgmGameStart As New AudioSource("Assets/game_start.mp3")
    Private ReadOnly bgmCleared As New AudioSource("Assets/level_cleared.mp3")
    Private ReadOnly bgmVictory As New AudioSource("Assets/victory_theme.mp3")
    Private ReadOnly sfxBoxPushed As New AudioSource("Assets/box_pushed.wav")

    ' Task delay helper
    Private elapsedDelay As Single = 0
    Private delayAction As Action = Nothing
    Private Sub TaskDelay(sec As Integer, action As Action)
        elapsedDelay = sec * 1000
        delayAction = action
    End Sub

    Friend Shared Sub Main()
        With New Program
            If .Construct(screenW:=320, screenH:=240, fullScreen:=True) Then .Start()
        End With
    End Sub
End Class